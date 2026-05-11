using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FidelSec.UI.ViewModels;

namespace FidelSec.UI
{
    public partial class MainWindow : Window
    {
        private bool _userForcedCompact = false;
        private bool _isKiosk = false;
        private WindowState  _preKioskState  = WindowState.Normal;
        private WindowStyle  _preKioskStyle  = WindowStyle.SingleBorderWindow;
        private ResizeMode   _preKioskResize = ResizeMode.CanResize;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ── Responsive layout ─────────────────────────────────────────

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_userForcedCompact) return;          // user manually toggled — respect their choice
            ApplyLayout(e.NewSize.Width < 900);
        }

        private void ApplyLayout(bool compact)
        {
            WideLayout.Visibility    = compact ? Visibility.Collapsed : Visibility.Visible;
            CompactLayout.Visibility = compact ? Visibility.Visible   : Visibility.Collapsed;

            if (DataContext is MainViewModel vm)
                vm.IsCompactMode = compact;
        }

        private void OnCompactToggleClick(object sender, RoutedEventArgs e)
        {
            bool currentlyCompact = CompactLayout.Visibility == Visibility.Visible;
            _userForcedCompact = true;
            ApplyLayout(!currentlyCompact);
        }

        // ── Kiosk / fullscreen mode ───────────────────────────────────

        private void OnKioskToggleClick(object sender, RoutedEventArgs e)
            => ToggleKiosk();

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
                ToggleKiosk();
            else if (e.Key == Key.Escape && _isKiosk)
                ToggleKiosk();
        }

        private void ToggleKiosk()
        {
            if (!_isKiosk)
            {
                _preKioskState  = WindowState;
                _preKioskStyle  = WindowStyle;
                _preKioskResize = ResizeMode;

                WindowStyle  = WindowStyle.None;
                ResizeMode   = ResizeMode.NoResize;
                WindowState  = WindowState.Maximized;
                _isKiosk     = true;
            }
            else
            {
                WindowStyle  = _preKioskStyle;
                ResizeMode   = _preKioskResize;
                WindowState  = _preKioskState;
                _isKiosk     = false;
            }

            if (DataContext is MainViewModel vm)
                vm.IsKioskMode = _isKiosk;
        }
    }
}
