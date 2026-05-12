using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace FidelSec.UI.Avalonia.ViewModels
{
    public partial class DeviceListViewModel : ObservableObject
    {
        private readonly IDeviceScanner _scanner;
        private readonly ILogger<DeviceListViewModel> _logger;
        private Window? _ownerWindow;

        public ObservableCollection<PhysicalDevice> Devices { get; } = new();

        [ObservableProperty] private PhysicalDevice? _selectedDevice;
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private string _scanStatus = "Nog niet gescand";

        public event Action<PhysicalDevice>? DeviceSelected;

        public DeviceListViewModel(
            IDeviceScanner scanner,
            ILogger<DeviceListViewModel> logger)
        {
            _scanner = scanner;
            _logger  = logger;
        }

        /// <summary>
        /// Sets the owner window so dialogs can be parented correctly.
        /// Must be called from the view's code-behind after DataContext is set.
        /// </summary>
        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        partial void OnSelectedDeviceChanged(PhysicalDevice? value)
        {
            if (value != null) DeviceSelected?.Invoke(value);
        }

        [RelayCommand]
        public async Task ScanDevicesAsync()
        {
            IsScanning  = true;
            ScanStatus  = "Scannen...";
            Devices.Clear();

            try
            {
                var devices = await _scanner.ScanDevicesAsync();
                foreach (var d in devices) Devices.Add(d);
                ScanStatus = $"Gevonden: {devices.Count} apparaat/apparaten";
                _logger.LogInformation("Device scan complete: {Count}", devices.Count);
            }
            catch (Exception ex)
            {
                ScanStatus = $"Scan mislukt: {ex.Message}";
                _logger.LogError(ex, "Device scan failed");
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        public async Task AddTestFileAsync()
        {
            try
            {
                if (_ownerWindow is null)
                {
                    _logger.LogWarning("No owner window set for file dialog.");
                    return;
                }

                var options = new FilePickerOpenOptions
                {
                    Title            = "Selecteer een testbestand",
                    AllowMultiple    = false,
                    FileTypeFilter   = new[] { FilePickerFileTypes.All }
                };

                var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(options);
                if (files is not { Count: > 0 }) return;

                var file = files[0];
                var localPath = file.Path.LocalPath;
                var info = new System.IO.FileInfo(localPath);
                long fileLength = info.Length;

                var device = new PhysicalDevice
                {
                    DevicePath        = localPath,
                    DeviceId          = System.IO.Path.GetFileName(localPath),
                    Model             = $"[TEST] {System.IO.Path.GetFileName(localPath)}",
                    SerialNumber      = "TEST-0001",
                    SizeBytes         = fileLength >= 0 ? (ulong)fileLength : 0UL,
                    LogicalSectorSize  = 512,
                    PhysicalSectorSize = 512,
                    InterfaceType     = "File",
                    MediaType         = "Virtual Test Source",
                    PartitionStyle    = PartitionStyle.Unknown,
                    IsReadOnly        = true
                };

                Devices.Add(device);
                SelectedDevice = device;
                ScanStatus = $"Testbestand: {System.IO.Path.GetFileName(localPath)} ({FormatBytes(fileLength)})";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddTestFile failed");
                ScanStatus = $"Fout: {ex.Message}";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1) { val /= 1024; idx++; }
            return $"{val:F1} {units[idx]}";
        }
    }
}
