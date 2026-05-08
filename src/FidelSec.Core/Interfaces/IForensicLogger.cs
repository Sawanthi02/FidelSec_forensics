using FidelSec.Core.Models;

namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// Contract for forensic logging and chain of custody report generation.
    /// All imaging events must be logged before writing to disk.
    /// </summary>
    public interface IForensicLogger
    {
        /// <summary>Log start of an imaging acquisition</summary>
        void LogAcquisitionStart(ImagingJob job);

        /// <summary>Log completion of an imaging acquisition with full results</summary>
        void LogAcquisitionComplete(ImagingJob job, ImagingResult result);

        /// <summary>Log a bad sector event</summary>
        void LogBadSector(Guid jobId, long sectorNumber, int retryCount);

        /// <summary>Log a general informational event</summary>
        void LogEvent(Guid jobId, string message, ForensicLogLevel level = ForensicLogLevel.Info);

        /// <summary>Export chain of custody log as JSON</summary>
        Task ExportJsonLogAsync(Guid jobId, string outputPath);

        /// <summary>Export chain of custody log as human-readable text</summary>
        Task ExportTextLogAsync(Guid jobId, string outputPath);
    }

    public enum ForensicLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }
}
