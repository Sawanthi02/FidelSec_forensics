using System;

namespace FidelSec.Core.Models
{
    /// <summary>
    /// Real-time progress snapshot for an imaging operation.
    /// Published via IProgress[ImagingProgress] at regular intervals.
    /// </summary>
    public class ImagingProgress
    {
        /// <summary>Job this progress report belongs to</summary>
        public Guid JobId { get; set; }

        /// <summary>Current state of the imaging job</summary>
        public ImagingState State { get; set; }

        /// <summary>Bytes read from source so far</summary>
        public long BytesRead { get; set; }

        /// <summary>Total bytes to read (equals device size)</summary>
        public long TotalBytes { get; set; }

        /// <summary>Current throughput in bytes per second</summary>
        public double SpeedBytesPerSecond { get; set; }

        /// <summary>Estimated time remaining</summary>
        public TimeSpan EstimatedTimeRemaining { get; set; }

        /// <summary>Elapsed time since job start</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>Number of bad sectors encountered</summary>
        public long BadSectorCount { get; set; }

        /// <summary>Current sector being processed</summary>
        public long CurrentSector { get; set; }

        /// <summary>Percentage complete (0–100)</summary>
        public double PercentComplete => TotalBytes > 0 ? (double)BytesRead / TotalBytes * 100.0 : 0;

        /// <summary>Human-readable speed string</summary>
        public string SpeedHuman => FormatSpeed(SpeedBytesPerSecond);

        /// <summary>Any error message if State == Failed</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Intermediate hash values (updated periodically)</summary>
        public Dictionary<HashAlgorithmType, string> IntermediateHashes { get; set; } = new();

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec >= 1_073_741_824) return $"{bytesPerSec / 1_073_741_824:F1} GB/s";
            if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
            if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:F1} KB/s";
            return $"{bytesPerSec:F0} B/s";
        }
    }

    /// <summary>Lifecycle state of an imaging job</summary>
    public enum ImagingState
    {
        Idle,
        Initializing,
        Imaging,
        Paused,
        Verifying,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Final result returned when imaging completes.
    /// Stored as part of the chain of custody log.
    /// </summary>
    public class ImagingResult
    {
        public Guid JobId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public long TotalBytesWritten { get; set; }
        public long BadSectorCount { get; set; }
        public Dictionary<HashAlgorithmType, string> SourceHashes { get; set; } = new();
        public Dictionary<HashAlgorithmType, string> ImageHashes { get; set; } = new();
        public bool HashesVerified { get; set; }
        public List<string> OutputFiles { get; set; } = new();
    }
}
