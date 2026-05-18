using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FidelSec.UI.Avalonia.ViewModels;

namespace FidelSec.UI.Avalonia.Views
{
    public partial class DeviceListView : UserControl
    {
        private bool _initialScanDone;

        public DeviceListView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (DataContext is DeviceListViewModel vm)
            {
                var window = this.FindAncestorOfType<Window>();
                if (window is not null)
                    vm.SetOwnerWindow(window);

                // Automatically scan on first show so the user sees results without clicking
                if (!_initialScanDone)
                {
                    _initialScanDone = true;
                    if (vm.ScanDevicesCommand.CanExecute(null))
                        vm.ScanDevicesCommand.Execute(null);
                }
            }
        }
    }
}
