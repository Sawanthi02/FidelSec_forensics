using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using FidelSec.Infrastructure.DiskAccess;
using FidelSec.Infrastructure.Linux.DiskAccess;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.Linux.DiskAccess
{
    /// <summary>
    /// Linux implementation of IDiskReaderFactory.
    /// Routes /dev/* paths to LinuxDiskReader and regular files to FileDiskReader.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class LinuxDiskReaderFactory : IDiskReaderFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public LinuxDiskReaderFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IDiskReader Create(string devicePath)
        {
            bool isBlockDevice = devicePath.StartsWith("/dev/",
                StringComparison.OrdinalIgnoreCase);

            if (isBlockDevice)
                return new LinuxDiskReader(devicePath,
                    _loggerFactory.CreateLogger<LinuxDiskReader>());

            // Regular file (test mode / FileDiskReader from shared Infrastructure)
            return new FileDiskReader(devicePath,
                _loggerFactory.CreateLogger<FileDiskReader>());
        }
    }
}
