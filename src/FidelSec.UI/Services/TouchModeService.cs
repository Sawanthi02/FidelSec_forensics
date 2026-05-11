using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FidelSec.UI.Services
{
    /// <summary>
    /// Detects whether the current device has a touch digitizer or is a Tablet PC,
    /// enabling the UI to switch into touch-optimised mode automatically.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class TouchModeService
    {
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_MAXIMUMTOUCHES = 95;   // max simultaneous touch points
        private const int SM_TABLETPC       = 86;   // non-zero on Tablet PC edition

        /// <summary>True when the OS reports at least one hardware touch point.</summary>
        public bool IsTouchDevice { get; }

        /// <summary>True when running on Windows Tablet PC platform.</summary>
        public bool IsTabletPc { get; }

        public TouchModeService()
        {
            IsTouchDevice = GetSystemMetrics(SM_MAXIMUMTOUCHES) > 0;
            IsTabletPc    = GetSystemMetrics(SM_TABLETPC) != 0;
        }
    }
}
