using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FidelSec.UI.Avalonia.ViewModels;

namespace FidelSec.UI.Avalonia.Views
{
    public partial class ImagingConfigView : UserControl
    {
        public ImagingConfigView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (DataContext is ImagingConfigViewModel vm)
            {
                var window = this.FindAncestorOfType<Window>();
                if (window is not null)
                    vm.SetOwnerWindow(window);
            }
        }
    }
}
