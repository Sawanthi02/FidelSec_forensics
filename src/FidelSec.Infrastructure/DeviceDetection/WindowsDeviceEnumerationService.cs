using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.DeviceDetection
{
    /// <summary>
    /// Windows implementation of IDeviceEnumerationService.
    /// Wraps WmiDeviceScanner and adds WMI event-based hot-plug monitoring.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsDeviceEnumerationService : IDeviceEnumerationService, IDisposable
    {
        private readonly IDeviceScanner _scanner;
        private readonly ILogger<WindowsDeviceEnumerationService> _logger;
        private System.Management.ManagementEventWatcher? _attachWatcher;
        private System.Management.ManagementEventWatcher? _detachWatcher;

        public event EventHandler<PhysicalDevice>? DeviceAttached;
        public event EventHandler<string>? DeviceRemoved;

        public WindowsDeviceEnumerationService(
            IDeviceScanner scanner,
            ILogger<WindowsDeviceEnumerationService> logger)
        {
            _scanner = scanner;
            _logger  = logger;
        }

        public Task<IReadOnlyList<PhysicalDevice>> EnumerateDevicesAsync(CancellationToken ct = default)
            => _scanner.ScanDevicesAsync(ct);

        public void StartMonitoring()
        {
            try
            {
                // WMI event watcher: new disk drives
                _attachWatcher = new System.Management.ManagementEventWatcher(
                    new System.Management.WqlEventQuery(
                        "__InstanceCreationEvent",
                        TimeSpan.FromSeconds(2),
                        "TargetInstance ISA 'Win32_DiskDrive'"));
                _attachWatcher.EventArrived += OnDeviceArrived;
                _attachWatcher.Start();

                // WMI event watcher: removed disk drives
                _detachWatcher = new System.Management.ManagementEventWatcher(
                    new System.Management.WqlEventQuery(
                        "__InstanceDeletionEvent",
                        TimeSpan.FromSeconds(2),
                        "TargetInstance ISA 'Win32_DiskDrive'"));
                _detachWatcher.EventArrived += OnDeviceRemoved;
                _detachWatcher.Start();

                _logger.LogInformation("WMI hot-plug monitoring started.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WMI hot-plug monitoring could not be started.");
            }
        }

        public void StopMonitoring()
        {
            _attachWatcher?.Stop();
            _detachWatcher?.Stop();
        }

        private void OnDeviceArrived(object sender,
            System.Management.EventArrivedEventArgs e)
        {
            try
            {
                var target = (System.Management.ManagementBaseObject)
                    e.NewEvent["TargetInstance"];
                string deviceId = target["DeviceID"]?.ToString() ?? string.Empty;

                _logger.LogInformation("Device attached: {DeviceId}", deviceId);

                // Re-scan to get full device info
                Task.Run(async () =>
                {
                    var device = await _scanner.GetDeviceAsync(deviceId);
                    if (device != null)
                        DeviceAttached?.Invoke(this, device);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing device attach event.");
            }
        }

        private void OnDeviceRemoved(object sender,
            System.Management.EventArrivedEventArgs e)
        {
            try
            {
                var target = (System.Management.ManagementBaseObject)
                    e.NewEvent["TargetInstance"];
                string deviceId = target["DeviceID"]?.ToString() ?? string.Empty;
                _logger.LogInformation("Device removed: {DeviceId}", deviceId);
                DeviceRemoved?.Invoke(this, deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing device remove event.");
            }
        }

        public void Dispose()
        {
            StopMonitoring();
            _attachWatcher?.Dispose();
            _detachWatcher?.Dispose();
        }
    }
}
