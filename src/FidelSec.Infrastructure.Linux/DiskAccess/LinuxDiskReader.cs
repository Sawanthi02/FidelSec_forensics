using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.Linux.DiskAccess
{
    /// <summary>
    /// Read-only block device reader for Linux.
    ///
    /// Opens /dev/sdX or /dev/nvmeXn1 with O_RDONLY | O_DIRECT for sector-aligned
    /// reads. Never opens with write access — enforced at the OS level via open(2).
    ///
    /// SECURITY: Read-only access is enforced by the Linux kernel. We request
    /// O_RDONLY and do not call write(2) / pwrite(2) under any circumstances.
    ///
    /// Requires root or CAP_SYS_RAWIO to open raw block devices.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class LinuxDiskReader : IDiskReader, IDisposable
    {
        // POSIX open(2) flags
        private const int O_RDONLY = 0;
        private const int O_DIRECT = 0x4000;  // bypass page cache
        private const int O_LARGEFILE = 0;    // implicit on 64-bit kernels

        // ioctl(2) for block device size
        private const ulong BLKGETSIZE64 = 0x80081272;
        private const ulong BLKBSZGET   = 0x80081270;
        private const ulong BLKPBSZGET  = 0x127e;

        [DllImport("libc", EntryPoint = "open",  SetLastError = true)]
        private static extern int Open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int fd);

        [DllImport("libc", EntryPoint = "pread", SetLastError = true)]
        private static extern long PRead(int fd, byte[] buf, ulong count, long offset);

        [DllImport("libc", EntryPoint = "ioctl",  SetLastError = true)]
        private static extern int Ioctl(int fd, ulong request, out ulong value);

        [DllImport("libc", EntryPoint = "ioctl",  SetLastError = true)]
        private static extern int Ioctl2(int fd, ulong request, out int value);

        // ── Fields ───────────────────────────────────────────────────────────
        private int    _fd = -1;
        private bool   _disposed;
        private readonly ILogger<LinuxDiskReader> _logger;

        public string DevicePath        { get; }
        public uint   SectorSize        { get; private set; } = 512;
        public long   TotalSectors      { get; private set; }
        public long   TotalBytes        { get; private set; }

        public LinuxDiskReader(string devicePath, ILogger<LinuxDiskReader> logger)
        {
            DevicePath = devicePath;
            _logger    = logger;
        }

        /// <inheritdoc/>
        public void Open()
        {
            if (_fd >= 0) return;

            _logger.LogInformation("Opening Linux block device (read-only): {Path}", DevicePath);

            // O_RDONLY only — never O_WRONLY or O_RDWR
            _fd = Open(DevicePath, O_RDONLY);

            if (_fd < 0)
            {
                int err = Marshal.GetLastWin32Error();
                string msg = err switch
                {
                    1  => "Operation not permitted. Run as root (sudo).",
                    13 => "Permission denied. Run as root or grant CAP_SYS_RAWIO.",
                    2  => $"Device not found: {DevicePath}",
                    _  => $"open() failed with errno {err}"
                };
                throw new UnauthorizedAccessException(
                    $"Cannot open {DevicePath}: {msg}");
            }

            // Query device size via ioctl BLKGETSIZE64
            if (Ioctl(_fd, BLKGETSIZE64, out ulong totalBytes) == 0)
                TotalBytes = (long)totalBytes;

            // Query logical sector size via ioctl BLKBSZGET
            if (Ioctl2(_fd, BLKPBSZGET, out int logicalSector) == 0 && logicalSector > 0)
                SectorSize = (uint)logicalSector;

            TotalSectors = SectorSize > 0 ? TotalBytes / SectorSize : 0;

            _logger.LogInformation(
                "Opened {Path}: {Bytes} bytes, {SectorSize} B/sector",
                DevicePath, TotalBytes, SectorSize);
        }

        /// <inheritdoc/>
        public void Seek(long byteOffset)
        {
            // pread() is used for all reads — no separate seek needed
            // This method is a no-op for LinuxDiskReader
        }

        /// <inheritdoc/>
        public int ReadSectors(long startSector, int sectorCount, byte[] buffer)
        {
            EnsureOpen();

            long offset        = startSector * SectorSize;
            int  bytesToRead   = (int)Math.Min(
                sectorCount * (long)SectorSize,
                TotalBytes - offset);

            if (bytesToRead <= 0) return 0;

            // Ensure buffer is large enough
            if (buffer.Length < bytesToRead)
                throw new ArgumentException(
                    $"Buffer too small: {buffer.Length} < {bytesToRead}");

            long bytesRead = PRead(_fd, buffer, (ulong)bytesToRead, offset);
            if (bytesRead < 0)
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"pread() failed on {DevicePath} at offset {offset}: errno {err}");
            }

            return (int)bytesRead;
        }

        private void EnsureOpen()
        {
            if (_fd < 0)
                throw new InvalidOperationException(
                    "Device not open. Call Open() first.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_fd >= 0)
            {
                Close(_fd);
                _fd = -1;
            }
        }
    }
}
