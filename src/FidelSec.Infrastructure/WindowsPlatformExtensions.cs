using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using FidelSec.Infrastructure.DeviceDetection;
using FidelSec.Infrastructure.DiskAccess;
using FidelSec.Infrastructure.Hashing;
using FidelSec.Infrastructure.Logging;
using FidelSec.Infrastructure.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure
{
    /// <summary>
    /// DI registration extensions for the Windows platform.
    /// Call AddWindowsPlatformServices() from the application startup.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WindowsPlatformExtensions
    {
        public static IServiceCollection AddWindowsPlatformServices(
            this IServiceCollection services)
        {
            // Shared cross-platform services
            services.AddSingleton<IHashingEngine, HashingEngine>();
            services.AddSingleton<IForensicLogger>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ForensicLogger>>();
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "FidelSec", "Logs");
                return new ForensicLogger(logger, logDir);
            });

            // Windows-specific device scanner (WMI)
            services.AddSingleton<IDeviceScanner, WmiDeviceScanner>();

            // Windows disk reader factory (Win32 + FileDiskReader for test files)
            services.AddSingleton<IDiskReaderFactory, WindowsDiskReaderFactory>();

            // Device enumeration service wrapping the scanner
            services.AddSingleton<IDeviceEnumerationService, WindowsDeviceEnumerationService>();

            return services;
        }
    }
}
