using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using FidelSec.Infrastructure.Hashing;
using FidelSec.Infrastructure.Logging;
using FidelSec.Infrastructure.Linux.DeviceDetection;
using FidelSec.Infrastructure.Linux.DiskAccess;
using Microsoft.Extensions.DependencyInjection;

namespace FidelSec.Infrastructure.Linux
{
    /// <summary>
    /// DI registration extensions for the Linux platform.
    /// Call AddLinuxPlatformServices() from the application startup.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static class LinuxPlatformExtensions
    {
        public static IServiceCollection AddLinuxPlatformServices(
            this IServiceCollection services)
        {
            // Shared cross-platform services
            services.AddSingleton<IHashingEngine, HashingEngine>();
            services.AddSingleton<IForensicLogger, ForensicLogger>();

            // Linux-specific device scanner (lsblk)
            services.AddSingleton<IDeviceScanner, LinuxDeviceScanner>();

            // Linux disk reader factory (/dev/* → LinuxDiskReader, files → FileDiskReader)
            services.AddSingleton<IDiskReaderFactory, LinuxDiskReaderFactory>();

            // Device enumeration service with poll-based hot-plug detection
            services.AddSingleton<IDeviceEnumerationService, LinuxDeviceEnumerationService>();

            return services;
        }
    }
}
