using System.Diagnostics;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using FidelSec.Infrastructure.DiskAccess;
using Microsoft.Extensions.Logging;

namespace FidelSec.ImagingEngine
{
    /// <summary>
    /// Core forensic imaging engine.
    ///
    /// Imaging pipeline:
    ///   1. Open source device as read-only via Win32DiskReader
    ///   2. Stream sectors in configurable buffer sizes
    ///   3. Feed each buffer to StreamingHasher (parallel hash computation)
    ///   4. Write buffer to output file (raw or split)
    ///   5. On bad sector: retry up to N times, then zero-fill if configured
    ///   6. On completion: optionally verify by re-reading the image and comparing hashes
    ///   7. Export forensic log
    ///
    /// Thread safety: Pause/Resume are thread-safe. StartImagingAsync is not
    /// reentrant — one job at a time per engine instance.
    /// </summary>
    public class RawImagingEngine : IImagingEngine
    {
        private readonly ILogger<RawImagingEngine> _logger;
        private readonly IHashingEngine _hashingEngine;
        private readonly IForensicLogger _forensicLogger;

        // Pause/resume gate
        private volatile bool _paused;
        private readonly SemaphoreSlim _pauseGate = new(1, 1);

        public RawImagingEngine(
            ILogger<RawImagingEngine> logger,
            IHashingEngine hashingEngine,
            IForensicLogger forensicLogger)
        {
            _logger = logger;
            _hashingEngine = hashingEngine;
            _forensicLogger = forensicLogger;
        }

        /// <inheritdoc/>
        public async Task<ImagingResult> StartImagingAsync(
            ImagingJob job,
            IProgress<ImagingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(job);

            _forensicLogger.LogAcquisitionStart(job);
            _logger.LogInformation("Imaging job {JobId} started for {Device}", job.JobId, job.SourceDevice.DevicePath);

            var result = new ImagingResult
            {
                JobId = job.JobId,
                StartTime = DateTime.UtcNow
            };

            ReportProgress(progress, new ImagingProgress
            {
                JobId = job.JobId,
                State = ImagingState.Initializing,
                TotalBytes = (long)job.SourceDevice.SizeBytes
            });

            try
            {
                // Ensure output directory exists
                string? outputDir = Path.GetDirectoryName(job.OutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                    Directory.CreateDirectory(outputDir);

                // Open the source — use FileDiskReader for regular files (test mode),
                // Win32DiskReader for physical devices (\\.\PhysicalDriveN)
                bool isFileSource = !job.SourceDevice.DevicePath.StartsWith(@"\\.\",
                    StringComparison.OrdinalIgnoreCase);

                IDiskReader diskReader = isFileSource
                    ? new FileDiskReader(job.SourceDevice.DevicePath,
                        _logger as ILogger<FileDiskReader>
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileDiskReader>.Instance)
                    : new Win32DiskReader(job.SourceDevice.DevicePath,
                        _logger as ILogger<Win32DiskReader>
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Win32DiskReader>.Instance);

                using (diskReader)
                {
                diskReader.Open();

                // Update device properties from actual geometry
                long totalBytes = diskReader.TotalBytes;
                uint sectorSize = diskReader.SectorSize;
                long totalSectors = diskReader.TotalSectors;

                _logger.LogInformation(
                    "Device geometry: {TotalBytes} bytes, {SectorSize} bytes/sector, {TotalSectors} sectors",
                    totalBytes, sectorSize, totalSectors);

                // Initialize streaming hasher for on-the-fly hashing
                using var sourceHasher = _hashingEngine.CreateStreamingHasher(job.HashAlgorithms);

                // Determine output file name(s)
                string outputExtension = job.Format == ImageFormat.Raw ? ".dd" : ".E01";
                string baseOutputPath = job.OutputPath.TrimEnd('\\', '/');
                if (!baseOutputPath.EndsWith(outputExtension, StringComparison.OrdinalIgnoreCase))
                    baseOutputPath += outputExtension;

                // Imaging buffer: must be multiple of sector size and at least 1 sector
                int sectorsPerBuffer = Math.Max(1, job.BufferSizeBytes / (int)sectorSize);
                int bufferSize = sectorsPerBuffer * (int)sectorSize;
                byte[] buffer = new byte[bufferSize];

                var outputFiles = new List<string>();
                Stream? currentOutput = null;
                long currentSegmentSize = 0;
                int segmentIndex = 0;

                string GetSegmentPath() => job.SplitImage
                    ? $"{Path.ChangeExtension(baseOutputPath, null)}.{segmentIndex:D3}{outputExtension}"
                    : baseOutputPath;

                try
                {
                    currentOutput = OpenOutputSegment(GetSegmentPath(), outputFiles);

                    long bytesWritten = 0;
                    long badSectors = 0;
                    long sector = 0;
                    var stopwatch = Stopwatch.StartNew();
                    var lastSpeedCheck = stopwatch.Elapsed;
                    long lastSpeedBytes = 0;
                    double currentSpeed = 0;

                    ReportProgress(progress, new ImagingProgress
                    {
                        JobId = job.JobId,
                        State = ImagingState.Imaging,
                        TotalBytes = totalBytes
                    });

                    while (sector < totalSectors)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Handle pause
                        await HandlePauseAsync(cancellationToken);

                        int sectorsToRead = (int)Math.Min(sectorsPerBuffer, totalSectors - sector);
                        int bytesToRead = sectorsToRead * (int)sectorSize;
                        int bytesRead = 0;
                        bool sectorReadOk = false;

                        // Retry loop for bad sectors
                        for (int attempt = 0; attempt <= job.BadSectorRetries; attempt++)
                        {
                            try
                            {
                                bytesRead = diskReader.ReadSectors(sector, sectorsToRead, buffer);
                                sectorReadOk = true;
                                break;
                            }
                            catch (IOException ioEx)
                            {
                                _forensicLogger.LogBadSector(job.JobId, sector, attempt);
                                _logger.LogWarning(ioEx,
                                    "Read error at sector {Sector}, attempt {Attempt}/{Max}",
                                    sector, attempt + 1, job.BadSectorRetries + 1);

                                if (attempt == job.BadSectorRetries)
                                {
                                    if (job.ZeroFillBadSectors)
                                    {
                                        // Zero-fill unreadable sector(s)
                                        Array.Clear(buffer, 0, bytesToRead);
                                        bytesRead = bytesToRead;
                                        badSectors += sectorsToRead;
                                        _logger.LogWarning(
                                            "Zero-filling {Count} sector(s) starting at {Sector}",
                                            sectorsToRead, sector);
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                                else
                                {
                                    // Back off before retry (forensic: don't hammer a failing disk)
                                    await Task.Delay(50 * (attempt + 1), cancellationToken);
                                }
                            }
                        }

                        // Feed data to hasher (on-the-fly)
                        sourceHasher.FeedData(buffer, 0, bytesRead);

                        // Handle segment rotation for split images
                        if (job.SplitImage && currentSegmentSize + bytesRead > job.SplitSegmentSizeBytes)
                        {
                            await currentOutput!.FlushAsync(cancellationToken);
                            currentOutput.Dispose();
                            segmentIndex++;
                            currentSegmentSize = 0;
                            currentOutput = OpenOutputSegment(GetSegmentPath(), outputFiles);
                        }

                        await currentOutput!.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        bytesWritten += bytesRead;
                        currentSegmentSize += bytesRead;
                        sector += sectorsToRead;

                        // Speed calculation (update every 500ms)
                        var elapsed = stopwatch.Elapsed;
                        if ((elapsed - lastSpeedCheck).TotalMilliseconds >= 500)
                        {
                            double intervalSec = (elapsed - lastSpeedCheck).TotalSeconds;
                            currentSpeed = (bytesWritten - lastSpeedBytes) / intervalSec;
                            lastSpeedBytes = bytesWritten;
                            lastSpeedCheck = elapsed;
                        }

                        double remaining = currentSpeed > 0
                            ? (totalBytes - bytesWritten) / currentSpeed
                            : 0;

                        // Report progress
                        var prog = new ImagingProgress
                        {
                            JobId = job.JobId,
                            State = ImagingState.Imaging,
                            BytesRead = bytesWritten,
                            TotalBytes = totalBytes,
                            SpeedBytesPerSecond = currentSpeed,
                            EstimatedTimeRemaining = TimeSpan.FromSeconds(remaining),
                            Elapsed = elapsed,
                            BadSectorCount = badSectors,
                            CurrentSector = sector,
                            IntermediateHashes = sourceHasher.GetIntermediateValues()
                        };
                        ReportProgress(progress, prog);
                    }

                    await currentOutput!.FlushAsync(cancellationToken);
                    currentOutput.Dispose();
                    currentOutput = null;

                    // Finalize source hashes
                    result.SourceHashes = sourceHasher.Finalize();
                    result.TotalBytesWritten = bytesWritten;
                    result.BadSectorCount = badSectors;
                    result.OutputFiles = outputFiles;

                    _logger.LogInformation("Imaging complete. Source hashes: {Hashes}",
                        string.Join(", ", result.SourceHashes.Select(kv => $"{kv.Key}={kv.Value}")));

                    // Optional post-imaging verification
                    if (job.VerifyAfterImaging)
                    {
                        ReportProgress(progress, new ImagingProgress
                        {
                            JobId = job.JobId,
                            State = ImagingState.Verifying,
                            TotalBytes = totalBytes
                        });

                        result.ImageHashes = await _hashingEngine.ComputeFileHashesAsync(
                            outputFiles[0],
                            job.HashAlgorithms,
                            cancellationToken: cancellationToken);

                        result.HashesVerified = result.SourceHashes.All(kv =>
                            result.ImageHashes.TryGetValue(kv.Key, out var imgHash) &&
                            imgHash.Equals(kv.Value, StringComparison.OrdinalIgnoreCase));

                        if (result.HashesVerified)
                            _logger.LogInformation("Hash verification PASSED");
                        else
                            _logger.LogError("Hash verification FAILED — image may be corrupt!");
                    }

                    result.Success = true;
                    result.EndTime = DateTime.UtcNow;

                    ReportProgress(progress, new ImagingProgress
                    {
                        JobId = job.JobId,
                        State = ImagingState.Completed,
                        BytesRead = bytesWritten,
                        TotalBytes = totalBytes,
                        IntermediateHashes = result.SourceHashes
                    });
                }
                finally
                {
                    currentOutput?.Dispose();
                }
                } // end using (diskReader)
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Imaging cancelled by user.";
                result.EndTime = DateTime.UtcNow;

                ReportProgress(progress, new ImagingProgress
                {
                    JobId = job.JobId,
                    State = ImagingState.Cancelled
                });

                _forensicLogger.LogEvent(job.JobId, "Imaging cancelled by user.", ForensicLogLevel.Warning);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;

                _logger.LogError(ex, "Imaging job {JobId} failed", job.JobId);

                ReportProgress(progress, new ImagingProgress
                {
                    JobId = job.JobId,
                    State = ImagingState.Failed,
                    ErrorMessage = ex.Message
                });

                _forensicLogger.LogEvent(job.JobId, $"Imaging failed: {ex.Message}", ForensicLogLevel.Error);
            }

            _forensicLogger.LogAcquisitionComplete(job, result);
            return result;
        }

        /// <inheritdoc/>
        public async Task<ImagingResult> VerifyImageAsync(
            string imagePath,
            Dictionary<HashAlgorithmType, string> expectedHashes,
            IProgress<ImagingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ImagingResult
            {
                JobId = Guid.NewGuid(),
                StartTime = DateTime.UtcNow,
                OutputFiles = { imagePath }
            };

            long totalBytes = new FileInfo(imagePath).Length;
            long processed = 0;

            var fileProgress = new Progress<long>(bytes =>
            {
                processed = bytes;
                ReportProgress(progress, new ImagingProgress
                {
                    JobId = result.JobId,
                    State = ImagingState.Verifying,
                    BytesRead = bytes,
                    TotalBytes = totalBytes
                });
            });

            result.ImageHashes = await _hashingEngine.ComputeFileHashesAsync(
                imagePath, expectedHashes.Keys.ToArray(), fileProgress, cancellationToken);

            result.HashesVerified = expectedHashes.All(kv =>
                result.ImageHashes.TryGetValue(kv.Key, out var actual) &&
                actual.Equals(kv.Value, StringComparison.OrdinalIgnoreCase));

            result.Success = result.HashesVerified;
            result.EndTime = DateTime.UtcNow;

            ReportProgress(progress, new ImagingProgress
            {
                JobId = result.JobId,
                State = result.HashesVerified ? ImagingState.Completed : ImagingState.Failed
            });

            return result;
        }

        /// <inheritdoc/>
        public void Pause()
        {
            _paused = true;
            _logger.LogInformation("Imaging paused.");
        }

        /// <inheritdoc/>
        public void Resume()
        {
            _paused = false;
            _pauseGate.Release();
            _logger.LogInformation("Imaging resumed.");
        }

        private async Task HandlePauseAsync(CancellationToken cancellationToken)
        {
            if (!_paused) return;

            // Wait until Resume() releases the semaphore
            await _pauseGate.WaitAsync(cancellationToken);
        }

        private static Stream OpenOutputSegment(string path, List<string> trackingList)
        {
            var fs = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4 * 1024 * 1024,
                useAsync: true);
            trackingList.Add(path);
            return fs;
        }

        private static void ReportProgress(IProgress<ImagingProgress>? progress, ImagingProgress value)
        {
            progress?.Report(value);
        }
    }
}
