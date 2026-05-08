using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;

namespace FidelSec.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the left-side device list panel.
    /// Populates from WMI and notifies when user selects a device.
    /// </summary>
    public partial class DeviceListViewModel : ObservableObject
    {
        private readonly IDeviceScanner _scanner;
        private readonly ILogger<DeviceListViewModel> _logger;

        public ObservableCollection<PhysicalDevice> Devices { get; } = new();

        [ObservableProperty]
        private PhysicalDevice? _selectedDevice;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _scanStatus = "Not scanned";

        public event Action<PhysicalDevice>? DeviceSelected;

        public DeviceListViewModel(IDeviceScanner scanner, ILogger<DeviceListViewModel> logger)
        {
            _scanner = scanner;
            _logger = logger;
        }

        partial void OnSelectedDeviceChanged(PhysicalDevice? value)
        {
            if (value != null)
                DeviceSelected?.Invoke(value);
        }

        [RelayCommand]
        public async Task ScanDevicesAsync()
        {
            IsScanning = true;
            ScanStatus = "Scanning...";
            Devices.Clear();

            try
            {
                var devices = await _scanner.ScanDevicesAsync();
                foreach (var d in devices)
                    Devices.Add(d);

                ScanStatus = $"Found {devices.Count} device(s)";
                _logger.LogInformation("Device scan complete: {Count} devices", devices.Count);
            }
            catch (Exception ex)
            {
                ScanStatus = $"Scan failed: {ex.Message}";
                _logger.LogError(ex, "Device scan failed");
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// Adds any file as a virtual "device" for testing purposes.
        /// Allows full pipeline testing without physical disk access.
        /// </summary>
        [RelayCommand]
        public void AddTestFile()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select a file to use as test source",
                    Filter = "All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                bool? result = dialog.ShowDialog(
                    System.Windows.Application.Current.MainWindow);

                if (result != true) return;

                var info = new FileInfo(dialog.FileName);
                long fileLength = info.Length;

                var device = new PhysicalDevice
                {
                    DevicePath = dialog.FileName,
                    DeviceId = Path.GetFileName(dialog.FileName),
                    Model = $"[TEST] {Path.GetFileName(dialog.FileName)}",
                    SerialNumber = "TEST-0001",
                    SizeBytes = fileLength >= 0 ? (ulong)fileLength : 0UL,
                    LogicalSectorSize = 512,
                    PhysicalSectorSize = 512,
                    InterfaceType = "File",
                    MediaType = "Virtual Test Source",
                    PartitionStyle = PartitionStyle.Unknown,
                    IsReadOnly = true
                };

                Devices.Add(device);
                SelectedDevice = device;
                ScanStatus = $"Test file: {Path.GetFileName(dialog.FileName)} ({FormatBytes(fileLength)})";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddTestFile failed");
                ScanStatus = $"Error: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Could not add test file:\n\n{ex.Message}",
                    "Error", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
    }
}
