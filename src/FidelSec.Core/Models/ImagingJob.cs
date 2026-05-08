using System;

namespace FidelSec.Core.Models
{
    /// <summary>
    /// Holds all configuration for a single imaging job.
    /// Passed to the imaging engine at start of acquisition.
    /// </summary>
    public class ImagingJob
    {
        /// <summary>Unique job identifier for logging and chain of custody</summary>
        public Guid JobId { get; set; } = Guid.NewGuid();

        /// <summary>Source device to image</summary>
        public PhysicalDevice SourceDevice { get; set; } = null!;

        /// <summary>Full output file path (without extension if split)</summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>Target image format</summary>
        public ImageFormat Format { get; set; } = ImageFormat.Raw;

        /// <summary>Whether to split output into multiple files</summary>
        public bool SplitImage { get; set; } = false;

        /// <summary>Split segment size in bytes (default 2 GB)</summary>
        public long SplitSegmentSizeBytes { get; set; } = 2L * 1024 * 1024 * 1024;

        /// <summary>Compression level for E01 (0=none, 9=max)</summary>
        public int CompressionLevel { get; set; } = 0;

        /// <summary>Buffer size for IO operations (default 1 MB)</summary>
        public int BufferSizeBytes { get; set; } = 1 * 1024 * 1024;

        /// <summary>Number of retry attempts for bad sectors</summary>
        public int BadSectorRetries { get; set; } = 3;

        /// <summary>Whether to zero-fill unreadable sectors instead of aborting</summary>
        public bool ZeroFillBadSectors { get; set; } = true;

        /// <summary>Hash algorithms to compute during imaging</summary>
        public HashAlgorithmType[] HashAlgorithms { get; set; } = { HashAlgorithmType.MD5, HashAlgorithmType.SHA256 };

        /// <summary>Whether to verify the image after completion</summary>
        public bool VerifyAfterImaging { get; set; } = true;

        // --- Case metadata for forensic chain of custody ---
        public string CaseNumber { get; set; } = string.Empty;
        public string EvidenceNumber { get; set; } = string.Empty;
        public string ExaminerName { get; set; } = string.Empty;
        public string ExaminerOrganization { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime AcquisitionTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Supported output image formats</summary>
    public enum ImageFormat
    {
        /// <summary>Raw bit-for-bit copy (DD format)</summary>
        Raw,
        /// <summary>Expert Witness Format (EnCase E01)</summary>
        E01
    }

    /// <summary>Supported hash algorithms</summary>
    public enum HashAlgorithmType
    {
        MD5,
        SHA1,
        SHA256
    }
}
