using FidelSec.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.DiskAccess
{
    /// <summary>
    /// File-backed disk reader for testing purposes.
    /// Treats any regular file as if it were a physical device.
    /// Allows full pipeline testing without Administrator rights or real hardware.
    /// </summary>
    public class FileDiskReader : IDiskReader
    {
        private FileStream? _stream;
        private readonly ILogger<FileDiskReader> _logger;
        private bool _disposed;

        public string DevicePath { get; }
        public uint SectorSize { get; } = 512;
        public long TotalSectors { get; private set; }
        public long TotalBytes { get; private set; }

        public FileDiskReader(string filePath, ILogger<FileDiskReader> logger)
        {
            DevicePath = filePath;
            _logger = logger;
        }

        public void Open()
        {
            _stream = new FileStream(DevicePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: false);
            TotalBytes = _stream.Length;
            TotalSectors = (TotalBytes + SectorSize - 1) / SectorSize;
            _logger.LogInformation("Opened file source: {Path} ({Bytes} bytes)", DevicePath, TotalBytes);
        }

        public void Seek(long byteOffset)
        {
            EnsureOpen();
            _stream!.Seek(byteOffset, SeekOrigin.Begin);
        }

        public int ReadSectors(long startSector, int sectorCount, byte[] buffer)
        {
            EnsureOpen();
            long byteOffset = startSector * SectorSize;
            _stream!.Seek(byteOffset, SeekOrigin.Begin);

            int bytesToRead = (int)Math.Min(sectorCount * (long)SectorSize, TotalBytes - byteOffset);
            if (bytesToRead <= 0) return 0;

            int totalRead = 0;
            while (totalRead < bytesToRead)
            {
                int read = _stream.Read(buffer, totalRead, bytesToRead - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            return totalRead;
        }

        private void EnsureOpen()
        {
            if (_stream == null) throw new InvalidOperationException("Call Open() first.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream?.Dispose();
        }
    }
}
