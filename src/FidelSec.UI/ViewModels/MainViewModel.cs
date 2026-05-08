using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows;

namespace FidelSec.UI.ViewModels
{
    /// <summary>
    /// Root ViewModel for the main application window.
    /// Coordinates between DeviceList, ImagingConfig, and ImagingProgress panels.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly ILogger<MainViewModel> _logger;

        [ObservableProperty]
        private DeviceListViewModel _deviceList;

        [ObservableProperty]
        private ImagingConfigViewModel _imagingConfig;

        [ObservableProperty]
        private ImagingProgressViewModel _imagingProgress;

        [ObservableProperty]
        private string _statusMessage = "Ready — select a device to begin.";

        [ObservableProperty]
        private bool _isAdminMode;

        public MainViewModel(
            ILogger<MainViewModel> logger,
            DeviceListViewModel deviceList,
            ImagingConfigViewModel imagingConfig,
            ImagingProgressViewModel imagingProgress)
        {
            _logger = logger;
            _deviceList = deviceList;
            _imagingConfig = imagingConfig;
            _imagingProgress = imagingProgress;

            // Wire up device selection → imaging config
            _deviceList.DeviceSelected += OnDeviceSelected;

            // Check admin status
            IsAdminMode = IsRunningAsAdministrator();
            if (!IsAdminMode)
            {
                StatusMessage = "WARNING: Not running as Administrator. Raw disk access will fail.";
                _logger.LogWarning("Application started without Administrator privileges.");
            }
        }

        private void OnDeviceSelected(PhysicalDevice device)
        {
            ImagingConfig.SourceDevice = device;
            StatusMessage = $"Selected: {device.Model} ({device.SizeHuman})";
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
