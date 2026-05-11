using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using FidelSec.UI.Services;
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
        private readonly TouchModeService _touch;

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

        /// <summary>True when the window is narrower than 900 px (compact/tablet mode).</summary>
        [ObservableProperty]
        private bool _isCompactMode;

        /// <summary>True when the window is running in fullscreen kiosk mode.</summary>
        [ObservableProperty]
        private bool _isKioskMode;

        /// <summary>Whether a hardware touch digitizer was detected at startup.</summary>
        public bool IsTouchDevice => _touch.IsTouchDevice;

        public MainViewModel(
            ILogger<MainViewModel> logger,
            TouchModeService touch,
            DeviceListViewModel deviceList,
            ImagingConfigViewModel imagingConfig,
            ImagingProgressViewModel imagingProgress)
        {
            _logger        = logger;
            _touch         = touch;
            _deviceList    = deviceList;
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

            if (_touch.IsTouchDevice)
                _logger.LogInformation("Touch digitizer detected — touch-optimised mode active.");
        }

        private void OnDeviceSelected(PhysicalDevice device)
        {
            ImagingConfig.SourceDevice = device;
            StatusMessage = $"Selected: {device.Model} ({device.SizeHuman})";
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity  = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal       = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
