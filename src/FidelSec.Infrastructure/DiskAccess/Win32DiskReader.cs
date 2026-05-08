using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using FidelSec.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.DiskAccess
{
    /// <summary>
    /// Low-level read-only disk reader using Win32 CreateFile API.
    /// Opens physical drives via \\.\PhysicalDriveN with GENERIC_READ and
    /// FILE_SHARE_READ | FILE_SHARE_WRITE to avoid write interference.
    ///
    /// SECURITY: This class enforces read-only access at the OS level.
    /// No write handle is ever opened to the source device.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class Win32DiskReader : IDiskReader
    {
        // ── Win32 Constants ──────────────────────────────────────────────────
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        private const int INVALID_HANDLE_VALUE = -1;

        // ── Win32 P/Invoke ───────────────────────────────────────────────────
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(
            SafeFileHandle hFile,
            long liDistanceToMove,
            out long lpNewFilePointer,
            uint dwMoveMethod);

        // ── Disk geometry structures ─────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct DISK_GEOMETRY
        {
            public long Cylinders;
            public uint MediaType;
            public uint TracksPerCylinder;
            public uint SectorsPerTrack;
            public uint BytesPerSector;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISK_GEOMETRY_EX
        {
            public DISK_GEOMETRY Geometry;
            public long DiskSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] Data;
        }

        // ── Fields ───────────────────────────────────────────────────────────
        private SafeFileHandle? _handle;
        private readonly ILogger<Win32DiskReader> _logger;
        private bool _disposed;

        public string DevicePath { get; }
        public uint SectorSize { get; private set; } = 512;
        public long TotalSectors { get; private set; }
        public long TotalBytes { get; private set; }

        public Win32DiskReader(string devicePath, ILogger<Win32DiskReader> logger)
        {
            DevicePath = devicePath;
            _logger = logger;
        }

        /// <inheritdoc/>
        public void Open()
        {
            if (_handle != null && !_handle.IsInvalid)
                return; // Already open

            _logger.LogInformation("Opening device for read-only access: {DevicePath}", DevicePath);

            // Open with GENERIC_READ only — never GENERIC_WRITE.
            // FILE_FLAG_NO_BUFFERING ensures sector-aligned reads, required for raw disk access.
            _handle = CreateFile(
                DevicePath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN,
                IntPtr.Zero);

            if (_handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                string message = error switch
                {
                    5 => "Access denied. Run as Administrator.",
                    32 => "Device is locked by another process.",
                    _ => $"Win32 error code: {error}"
                };
                throw new UnauthorizedAccessException(
                    $"Cannot open device '{DevicePath}': {message}");
            }

            ReadDiskGeometry();
            _logger.LogInformation(
                "Device opened: SectorSize={SectorSize}, TotalBytes={TotalBytes}",
                SectorSize, TotalBytes);
        }

        private void ReadDiskGeometry()
        {
            int structSize = Marshal.SizeOf<DISK_GEOMETRY_EX>() + 128;
            IntPtr buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                bool ok = DeviceIoControl(
                    _handle!,
                    IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                    IntPtr.Zero, 0,
                    buffer, (uint)structSize,
                    out _,
                    IntPtr.Zero);

                if (!ok)
                {
                    _logger.LogWarning("IOCTL_DISK_GET_DRIVE_GEOMETRY_EX failed, using defaults");
                    return;
                }

                var geomEx = Marshal.PtrToStructure<DISK_GEOMETRY_EX>(buffer);
                SectorSize = geomEx.Geometry.BytesPerSector > 0
                    ? geomEx.Geometry.BytesPerSector
                    : 512;
                TotalBytes = geomEx.DiskSize;
                TotalSectors = TotalBytes / SectorSize;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <inheritdoc/>
        public void Seek(long byteOffset)
        {
            EnsureOpen();
            // Byte offset must be sector-aligned when FILE_FLAG_NO_BUFFERING is used
            if (byteOffset % SectorSize != 0)
                throw new ArgumentException(
                    $"Seek offset {byteOffset} is not aligned to sector size {SectorSize}");

            if (!SetFilePointerEx(_handle!, byteOffset, out _, 0 /* FILE_BEGIN */))
                throw new IOException($"SetFilePointerEx failed: {Marshal.GetLastWin32Error()}");
        }

        /// <inheritdoc/>
        public int ReadSectors(long startSector, int sectorCount, byte[] buffer)
        {
            EnsureOpen();

            long byteOffset = startSector * SectorSize;
            int bytesToRead = sectorCount * (int)SectorSize;

            if (buffer.Length < bytesToRead)
                throw new ArgumentException("Buffer too small for requested sector count");

            // Sector-align the seek
            Seek(byteOffset);

            bool ok = ReadFile(_handle!, buffer, (uint)bytesToRead, out uint bytesRead, IntPtr.Zero);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"ReadFile failed at sector {startSector}: Win32 error {error}");
            }

            return (int)bytesRead;
        }

        private void EnsureOpen()
        {
            if (_handle == null || _handle.IsInvalid || _handle.IsClosed)
                throw new InvalidOperationException("Device is not open. Call Open() first.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
            _logger.LogDebug("Device handle closed: {DevicePath}", DevicePath);
        }
    }
}
