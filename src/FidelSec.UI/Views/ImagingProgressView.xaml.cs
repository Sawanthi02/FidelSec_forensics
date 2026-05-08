using System.Windows;
using System.Windows.Controls;
using FidelSec.Core.Models;
using FidelSec.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FidelSec.UI.Views
{
    public partial class ImagingProgressView : UserControl
    {
        public ImagingProgressView()
        {
            InitializeComponent();

            // Wire Start button click manually (requires access to config VM)
            if (StartButton != null)
                StartButton.Click += OnStartClicked;
        }

        private async void OnStartClicked(object sender, RoutedEventArgs e)
        {
            // Resolve the config VM from DI via the main window's DataContext
            var mainVm = Application.Current.MainWindow?.DataContext as MainViewModel;
            if (mainVm == null) return;

            var job = mainVm.ImagingConfig.BuildJob(out string? error);
            if (job == null)
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DataContext is ImagingProgressViewModel vm)
                await vm.StartImagingAsync(job);
        }
    }
}
