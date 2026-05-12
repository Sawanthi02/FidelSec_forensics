using System.Runtime.InteropServices;

namespace FidelSec.Core.Services
{
    /// <summary>
    /// Cross-platform OS detection helpers.
    /// Use these instead of conditional compilation to keep code readable.
    /// </summary>
    public static class PlatformInfo
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux   => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsMacOS   => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>
        /// True when the current process has elevated (root / Administrator) privileges.
        /// </summary>
        public static bool IsElevated
        {
            get
            {
                if (IsWindows)
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(
                        System.Security.Principal.WindowsBuiltInRole.Administrator);
                }

                // On Linux/macOS: effective UID 0 = root
                return Environment.GetEnvironmentVariable("EUID") == "0"
                    || GetEffectiveUid() == 0;
            }
        }

        /// <summary>
        /// Returns the device path prefix for physical drives on the current OS.
        /// Windows: \\.\PhysicalDrive   Linux: /dev/sd or /dev/nvme
        /// </summary>
        public static string PhysicalDevicePrefix =>
            IsWindows ? @"\\.\PhysicalDrive" : "/dev/";

        /// <summary>
        /// Returns true when the path looks like a raw block device (not a regular file).
        /// </summary>
        public static bool IsBlockDevicePath(string path)
        {
            if (IsWindows)
                return path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase);
            return path.StartsWith("/dev/", StringComparison.Ordinal);
        }

        // libc getuid() for Linux / macOS
        [DllImport("libc", EntryPoint = "getuid", SetLastError = false)]
        private static extern uint GetEffectiveUid();
    }
}
