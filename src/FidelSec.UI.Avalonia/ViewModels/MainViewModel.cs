using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Models;
using FidelSec.Core.Services;

namespace FidelSec.UI.Avalonia.ViewModels
{
    /// <summary>
    /// Root ViewModel — coordinates device selection, config and progress panels.
    /// Fully cross-platform: no WPF or Windows-specific references.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private DeviceListViewModel _deviceList;
        [ObservableProperty] private ImagingConfigViewModel _imagingConfig;
        [ObservableProperty] private ImagingProgressViewModel _imagingProgress;
        [ObservableProperty] private string _statusMessage = "Gereed — selecteer een apparaat.";
        [ObservableProperty] private bool _isElevated;
        [ObservableProperty] private bool _isCompactMode;
        [ObservableProperty] private bool _isKioskMode;

        public bool IsWindows => PlatformInfo.IsWindows;
        public bool IsLinux   => PlatformInfo.IsLinux;
        public string PlatformLabel => PlatformInfo.IsWindows ? "Windows" : "Linux";
        public string ElevatedLabel => PlatformInfo.IsWindows ? "ADMINISTRATOR" : "ROOT";

        public MainViewModel(
            DeviceListViewModel deviceList,
            ImagingConfigViewModel imagingConfig,
            ImagingProgressViewModel imagingProgress)
        {
            _deviceList      = deviceList;
            _imagingConfig   = imagingConfig;
            _imagingProgress = imagingProgress;

            _deviceList.DeviceSelected += OnDeviceSelected;

            IsElevated = PlatformInfo.IsElevated;
            if (!IsElevated)
            {
                StatusMessage = PlatformInfo.IsWindows
                    ? "WAARSCHUWING: Geen Administrator-rechten. Start opnieuw als beheerder."
                    : "WAARSCHUWING: Geen root-rechten. Start opnieuw met sudo.";
            }
        }

        private void OnDeviceSelected(PhysicalDevice device)
        {
            ImagingConfig.SourceDevice = device;
            StatusMessage = $"Geselecteerd: {device.Model} ({device.SizeHuman})";
        }

        [RelayCommand]
        public async Task StartImagingAsync()
        {
            var job = ImagingConfig.BuildJob();
            if (job is null)
            {
                StatusMessage = "Selecteer een apparaat en uitvoerpad voor u start.";
                return;
            }
            StatusMessage = $"Acquisitie gestart: {job.JobId}";
            await ImagingProgress.StartImagingCommand.ExecuteAsync(job);
        }
    }
}
