using FidelSec.Core.Models;

namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// High-level service that enumerates storage devices and monitors for hot-plug events.
    /// Wraps IDeviceScanner with event-based notifications for UI binding.
    /// </summary>
    public interface IDeviceEnumerationService
    {
        /// <summary>Raised when a new device is attached (udev / WMI watch).</summary>
        event EventHandler<PhysicalDevice> DeviceAttached;

        /// <summary>Raised when a device is removed.</summary>
        event EventHandler<string> DeviceRemoved;

        /// <summary>Perform a one-shot synchronous device scan.</summary>
        Task<IReadOnlyList<PhysicalDevice>> EnumerateDevicesAsync(CancellationToken ct = default);

        /// <summary>Start background monitoring for hot-plug events.</summary>
        void StartMonitoring();

        /// <summary>Stop background monitoring.</summary>
        void StopMonitoring();
    }
}
