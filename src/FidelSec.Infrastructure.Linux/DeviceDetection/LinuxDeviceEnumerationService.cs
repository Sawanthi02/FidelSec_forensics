using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using FidelSec.Infrastructure.Linux.DeviceDetection;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.Linux.DeviceDetection
{
    /// <summary>
    /// Linux implementation of IDeviceEnumerationService.
    /// Uses a background thread polling lsblk for hot-plug detection
    /// (udev netlink monitoring can be added for production use).
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class LinuxDeviceEnumerationService : IDeviceEnumerationService, IDisposable
    {
        private readonly IDeviceScanner _scanner;
        private readonly ILogger<LinuxDeviceEnumerationService> _logger;

        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;
        private IReadOnlyList<PhysicalDevice> _lastSnapshot = Array.Empty<PhysicalDevice>();

        public event EventHandler<PhysicalDevice>? DeviceAttached;
        public event EventHandler<string>? DeviceRemoved;

        public LinuxDeviceEnumerationService(
            IDeviceScanner scanner,
            ILogger<LinuxDeviceEnumerationService> logger)
        {
            _scanner = scanner;
            _logger  = logger;
        }

        public Task<IReadOnlyList<PhysicalDevice>> EnumerateDevicesAsync(
            CancellationToken ct = default)
            => _scanner.ScanDevicesAsync(ct);

        public void StartMonitoring()
        {
            _monitorCts  = new CancellationTokenSource();
            _monitorTask = Task.Run(() => PollLoopAsync(_monitorCts.Token));
            _logger.LogInformation("Linux device polling monitor started (2 s interval).");
        }

        public void StopMonitoring()
        {
            _monitorCts?.Cancel();
            _monitorTask?.Wait(TimeSpan.FromSeconds(5));
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            _lastSnapshot = await _scanner.ScanDevicesAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    var current = await _scanner.ScanDevicesAsync(ct);

                    // Detect added devices
                    foreach (var d in current)
                    {
                        if (!_lastSnapshot.Any(s => s.DevicePath == d.DevicePath))
                        {
                            _logger.LogInformation("Device attached: {Path}", d.DevicePath);
                            DeviceAttached?.Invoke(this, d);
                        }
                    }

                    // Detect removed devices
                    foreach (var d in _lastSnapshot)
                    {
                        if (!current.Any(c => c.DevicePath == d.DevicePath))
                        {
                            _logger.LogInformation("Device removed: {Path}", d.DevicePath);
                            DeviceRemoved?.Invoke(this, d.DevicePath);
                        }
                    }

                    _lastSnapshot = current;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error in device monitor poll loop.");
                }
            }
        }

        public void Dispose()
        {
            StopMonitoring();
            _monitorCts?.Dispose();
        }
    }
}
