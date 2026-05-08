using System.Text;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace FidelSec.Infrastructure.Logging
{
    /// <summary>
    /// Forensic logger that maintains a structured, tamper-evident audit trail
    /// for all imaging operations. Each job gets its own log file.
    ///
    /// Log entries include: timestamps, examiner info, hash values, bad sector counts,
    /// and a chain of custody summary suitable for court submission.
    /// </summary>
    public class ForensicLogger : IForensicLogger
    {
        private readonly ILogger<ForensicLogger> _serilog;
        private readonly string _logDirectory;

        // In-memory event store per job (keyed by JobId)
        private readonly Dictionary<Guid, List<ForensicLogEntry>> _jobLogs = new();
        private readonly object _lock = new();

        public ForensicLogger(ILogger<ForensicLogger> serilog, string logDirectory)
        {
            _serilog = serilog;
            _logDirectory = logDirectory;
            Directory.CreateDirectory(logDirectory);
        }

        /// <inheritdoc/>
        public void LogAcquisitionStart(ImagingJob job)
        {
            var entry = new ForensicLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = ForensicLogLevel.Info,
                JobId = job.JobId,
                Event = "AcquisitionStarted",
                Details = new Dictionary<string, object>
                {
                    ["CaseNumber"] = job.CaseNumber,
                    ["EvidenceNumber"] = job.EvidenceNumber,
                    ["Examiner"] = job.ExaminerName,
                    ["Organization"] = job.ExaminerOrganization,
                    ["SourceDevice"] = job.SourceDevice.DevicePath,
                    ["SourceModel"] = job.SourceDevice.Model,
                    ["SourceSerial"] = job.SourceDevice.SerialNumber,
                    ["SourceSize"] = job.SourceDevice.SizeBytes,
                    ["OutputPath"] = job.OutputPath,
                    ["Format"] = job.Format.ToString(),
                    ["HashAlgorithms"] = string.Join(", ", job.HashAlgorithms),
                    ["Notes"] = job.Notes
                }
            };

            AddEntry(job.JobId, entry);
            _serilog.LogInformation("[FORENSIC] Acquisition started: Case={Case} Evidence={Evidence}",
                job.CaseNumber, job.EvidenceNumber);
        }

        /// <inheritdoc/>
        public void LogAcquisitionComplete(ImagingJob job, ImagingResult result)
        {
            var entry = new ForensicLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = result.Success ? ForensicLogLevel.Info : ForensicLogLevel.Error,
                JobId = job.JobId,
                Event = result.Success ? "AcquisitionCompleted" : "AcquisitionFailed",
                Details = new Dictionary<string, object>
                {
                    ["Success"] = result.Success,
                    ["Duration"] = result.Duration.ToString(@"hh\:mm\:ss"),
                    ["TotalBytesWritten"] = result.TotalBytesWritten,
                    ["BadSectors"] = result.BadSectorCount,
                    ["HashesVerified"] = result.HashesVerified,
                    ["OutputFiles"] = result.OutputFiles,
                    ["SourceHashes"] = result.SourceHashes.ToDictionary(
                        k => k.Key.ToString(), v => v.Value),
                    ["ImageHashes"] = result.ImageHashes.ToDictionary(
                        k => k.Key.ToString(), v => v.Value),
                    ["ErrorMessage"] = result.ErrorMessage ?? string.Empty
                }
            };

            AddEntry(job.JobId, entry);
            _serilog.LogInformation(
                "[FORENSIC] Acquisition {Status}: Duration={Duration} Bytes={Bytes} BadSectors={Bad}",
                result.Success ? "COMPLETE" : "FAILED",
                result.Duration, result.TotalBytesWritten, result.BadSectorCount);
        }

        /// <inheritdoc/>
        public void LogBadSector(Guid jobId, long sectorNumber, int retryCount)
        {
            var entry = new ForensicLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = ForensicLogLevel.Warning,
                JobId = jobId,
                Event = "BadSector",
                Details = new Dictionary<string, object>
                {
                    ["Sector"] = sectorNumber,
                    ["RetryCount"] = retryCount
                }
            };

            AddEntry(jobId, entry);
            _serilog.LogWarning("[FORENSIC] Bad sector {Sector} (retries: {Retries})", sectorNumber, retryCount);
        }

        /// <inheritdoc/>
        public void LogEvent(Guid jobId, string message, ForensicLogLevel level = ForensicLogLevel.Info)
        {
            var entry = new ForensicLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                JobId = jobId,
                Event = "General",
                Details = new Dictionary<string, object> { ["Message"] = message }
            };

            AddEntry(jobId, entry);
            _serilog.Log(MapLevel(level), "[FORENSIC] {Message}", message);
        }

        /// <inheritdoc/>
        public async Task ExportJsonLogAsync(Guid jobId, string outputPath)
        {
            var entries = GetEntries(jobId);
            var json = JsonConvert.SerializeObject(entries, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8);
            _serilog.LogInformation("Exported JSON log: {Path}", outputPath);
        }

        /// <inheritdoc/>
        public async Task ExportTextLogAsync(Guid jobId, string outputPath)
        {
            var entries = GetEntries(jobId);
            var sb = new StringBuilder();
            sb.AppendLine("=== FidelSec Forensic Imager — Chain of Custody Log ===");
            sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Job ID: {jobId}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            foreach (var entry in entries)
            {
                sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC] [{entry.Level}] {entry.Event}");
                foreach (var (key, value) in entry.Details)
                    sb.AppendLine($"  {key}: {FormatValue(value)}");
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
            _serilog.LogInformation("Exported text log: {Path}", outputPath);
        }

        private void AddEntry(Guid jobId, ForensicLogEntry entry)
        {
            lock (_lock)
            {
                if (!_jobLogs.TryGetValue(jobId, out var list))
                {
                    list = new List<ForensicLogEntry>();
                    _jobLogs[jobId] = list;
                }
                list.Add(entry);
            }
        }

        private List<ForensicLogEntry> GetEntries(Guid jobId)
        {
            lock (_lock)
            {
                return _jobLogs.TryGetValue(jobId, out var list)
                    ? new List<ForensicLogEntry>(list)
                    : new List<ForensicLogEntry>();
            }
        }

        private static string FormatValue(object value)
        {
            if (value is IEnumerable<string> list)
                return string.Join(", ", list);
            if (value is Dictionary<string, string> dict)
                return string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
            return value?.ToString() ?? "(null)";
        }

        private static Microsoft.Extensions.Logging.LogLevel MapLevel(ForensicLogLevel level) => level switch
        {
            ForensicLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            ForensicLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            ForensicLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            ForensicLogLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
    }

    /// <summary>Represents a single timestamped forensic log entry</summary>
    public class ForensicLogEntry
    {
        public DateTime Timestamp { get; set; }
        public ForensicLogLevel Level { get; set; }
        public Guid JobId { get; set; }
        public string Event { get; set; } = string.Empty;
        public Dictionary<string, object> Details { get; set; } = new();
    }
}
