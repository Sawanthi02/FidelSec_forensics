using FidelSec.Core.Models;

namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// Contract for enumerating and inspecting physical storage devices.
    /// Implementations use WMI and SetupAPI to discover devices.
    /// </summary>
    public interface IDeviceScanner
    {
        /// <summary>
        /// Returns all physical disk drives visible to the OS.
        /// Excludes optical drives and virtual disk images.
        /// </summary>
        Task<IReadOnlyList<PhysicalDevice>> ScanDevicesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes a single device entry to detect hot-plug changes.
        /// </summary>
        Task<PhysicalDevice?> GetDeviceAsync(string devicePath, CancellationToken cancellationToken = default);
    }
}
