namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// Provides low-level read-only access to a physical disk device.
    /// All implementations MUST open the device with GENERIC_READ and no write sharing.
    /// </summary>
    public interface IDiskReader : IDisposable
    {
        /// <summary>Device path this reader is bound to</summary>
        string DevicePath { get; }

        /// <summary>Sector size in bytes</summary>
        uint SectorSize { get; }

        /// <summary>Total number of sectors</summary>
        long TotalSectors { get; }

        /// <summary>Total size in bytes</summary>
        long TotalBytes { get; }

        /// <summary>
        /// Opens the device for reading. Must be called before ReadSectors.
        /// Will throw UnauthorizedAccessException if not running as Administrator.
        /// </summary>
        void Open();

        /// <summary>
        /// Reads a contiguous range of sectors into the provided buffer.
        /// </summary>
        /// <param name="startSector">Zero-based sector index</param>
        /// <param name="sectorCount">Number of sectors to read</param>
        /// <param name="buffer">Buffer to fill — must be at least sectorCount * SectorSize bytes</param>
        /// <returns>Number of bytes actually read</returns>
        int ReadSectors(long startSector, int sectorCount, byte[] buffer);

        /// <summary>
        /// Seeks to an absolute byte offset (must be sector-aligned).
        /// </summary>
        void Seek(long byteOffset);
    }
}
