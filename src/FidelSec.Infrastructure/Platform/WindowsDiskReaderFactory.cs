using FidelSec.Core.Interfaces;
using FidelSec.Infrastructure.DiskAccess;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.Platform
{
    /// <summary>
    /// Windows implementation of IDiskReaderFactory.
    /// Selects Win32DiskReader for raw device paths (\\.\PhysicalDriveN)
    /// and FileDiskReader for regular files (test mode).
    /// </summary>
    public class WindowsDiskReaderFactory : IDiskReaderFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public WindowsDiskReaderFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IDiskReader Create(string devicePath)
        {
            bool isRawDevice = devicePath.StartsWith(@"\\.\",
                StringComparison.OrdinalIgnoreCase);

            if (isRawDevice)
                return new Win32DiskReader(devicePath,
                    _loggerFactory.CreateLogger<Win32DiskReader>());

            return new FileDiskReader(devicePath,
                _loggerFactory.CreateLogger<FileDiskReader>());
        }
    }
}
