namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// Factory that creates the correct IDiskReader for the current OS.
    /// Injected into ImagingEngine so it never references OS-specific types directly.
    /// </summary>
    public interface IDiskReaderFactory
    {
        /// <summary>
        /// Creates a read-only disk reader for the given device path.
        /// On Windows: \\.\PhysicalDriveN → Win32DiskReader, regular file → FileDiskReader.
        /// On Linux:   /dev/sdX / /dev/nvmeXn1 → LinuxDiskReader, regular file → FileDiskReader.
        /// </summary>
        IDiskReader Create(string devicePath);
    }
}
