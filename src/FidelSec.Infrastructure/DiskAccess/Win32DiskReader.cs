using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using FidelSec.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

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
        // NOTE: FILE_FLAG_NO_BUFFERING is intentionally NOT used.
        // It requires the read buffer to be aligned to the physical sector size (512/4096 bytes).
        // .NET managed byte[] arrays are NOT guaranteed to be sector-aligned, which causes
        // ReadFile to return ERROR_INVALID_PARAMETER (87) on every call.
        // The physical device path (\\.\.PhysicalDriveN) already bypasses the filesystem cache,
        // so FILE_FLAG_NO_BUFFERING provides no additional forensic benefit.
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        // IOCTL_DISK_GET_LENGTH_INFO: more reliable disk size than GEOMETRY_EX on some controllers
        private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
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

        // Use IntPtr-based overload so we can read into an offset within a pinned buffer.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            IntPtr lpBuffer,
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
                FILE_FLAG_SEQUENTIAL_SCAN,
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
                "Device opened: SectorSize={SectorSize}, TotalBytes={TotalBytes}, TotalSectors={TotalSectors}",
                SectorSize, TotalBytes, TotalSectors);
            LogMbrSignature();
        }

        private void ReadDiskGeometry()
        {
            // Step 1: IOCTL_DISK_GET_DRIVE_GEOMETRY_EX — sector size + initial disk size
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
                    _logger.LogWarning("IOCTL_DISK_GET_DRIVE_GEOMETRY_EX failed (Win32 error {Error}), using defaults",
                        Marshal.GetLastWin32Error());
                }
                else
                {
                    var geomEx = Marshal.PtrToStructure<DISK_GEOMETRY_EX>(buffer);
                    SectorSize = geomEx.Geometry.BytesPerSector > 0
                        ? geomEx.Geometry.BytesPerSector
                        : 512;
                    TotalBytes = geomEx.DiskSize;
                    TotalSectors = TotalBytes / SectorSize;
                    _logger.LogDebug(
                        "GEOMETRY_EX: Cylinders={Cyl} Tracks={T} SectorsPerTrack={SPT} BytesPerSector={BPS} DiskSize={DS}",
                        geomEx.Geometry.Cylinders, geomEx.Geometry.TracksPerCylinder,
                        geomEx.Geometry.SectorsPerTrack, geomEx.Geometry.BytesPerSector, geomEx.DiskSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            // Step 2: IOCTL_DISK_GET_LENGTH_INFO — authoritative byte count, overrides GEOMETRY_EX
            // This is the value used by dd, FTK Imager, and Guymager as the true disk size.
            IntPtr lengthBuf = Marshal.AllocHGlobal(8);
            try
            {
                bool ok = DeviceIoControl(
                    _handle!,
                    IOCTL_DISK_GET_LENGTH_INFO,
                    IntPtr.Zero, 0,
                    lengthBuf, 8,
                    out _,
                    IntPtr.Zero);

                if (ok)
                {
                    long lengthBytes = Marshal.ReadInt64(lengthBuf);
                    if (lengthBytes > 0 && lengthBytes != TotalBytes)
                    {
                        _logger.LogInformation(
                            "DISK_GET_LENGTH_INFO reports {Length} bytes (GEOMETRY_EX reported {Old}). Using LENGTH_INFO value.",
                            lengthBytes, TotalBytes);
                        TotalBytes = lengthBytes;
                    }
                    else if (lengthBytes > 0)
                    {
                        TotalBytes = lengthBytes;
                    }
                    TotalSectors = TotalBytes / SectorSize;
                }
                else
                {
                    _logger.LogDebug("IOCTL_DISK_GET_LENGTH_INFO failed (Win32 error {Error}), keeping GEOMETRY_EX size",
                        Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(lengthBuf);
            }

            if (TotalBytes == 0)
                _logger.LogWarning("Could not determine disk size via IOCTL. TotalBytes=0. Check permissions.");
        }

        /// <summary>
        /// Reads sector 0 and logs whether a valid MBR or GPT signature is present.
        /// This is a diagnostic check — it does not abort imaging if the signature is missing.
        /// </summary>
        private void LogMbrSignature()
        {
            if (TotalBytes < 512) return;
            try
            {
                byte[] sector0 = new byte[512];
                Seek(0);
                var pinHandle = GCHandle.Alloc(sector0, GCHandleType.Pinned);
                try
                {
                    bool ok = ReadFile(_handle!, pinHandle.AddrOfPinnedObject(), 512, out uint bytesRead, IntPtr.Zero);
                    if (ok && bytesRead == 512)
                    {
                        bool hasMbrSignature = sector0[510] == 0x55 && sector0[511] == 0xAA;
                        // GPT protective MBR: partition type 0xEE at offset 446+4
                        bool isGptProtective = hasMbrSignature && sector0[450] == 0xEE;
                        if (isGptProtective)
                            _logger.LogInformation("Sector 0: GPT protective MBR detected (0x55 0xAA + type 0xEE). Disk uses GPT.");
                        else if (hasMbrSignature)
                            _logger.LogInformation("Sector 0: Valid MBR boot signature detected (0x55 0xAA at offset 510).");
                        else
                            _logger.LogWarning(
                                "Sector 0: MBR boot signature NOT found (got 0x{B510:X2} 0x{B511:X2} at offset 510). " +
                                "Disk may be unpartitioned, formatted with a non-standard layout, or the read is incorrect.",
                                sector0[510], sector0[511]);

                        // Seek back to start for actual imaging
                        Seek(0);
                    }
                    else
                    {
                        _logger.LogWarning("Could not read sector 0 for MBR check (ok={Ok}, bytes={B})", ok, bytesRead);
                    }
                }
                finally
                {
                    pinHandle.Free();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MBR signature check failed (non-fatal)");
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

            Seek(byteOffset);

            // Pin the managed buffer so the GC cannot move it during the P/Invoke call.
            // We loop because ReadFile may legally return fewer bytes than requested
            // (e.g. at the end of a disk, or with certain USB/NVMe controllers).
            // Not completing the full read would create gaps in the output image.
            var pinHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                int totalBytesRead = 0;
                while (totalBytesRead < bytesToRead)
                {
                    IntPtr bufPtr = IntPtr.Add(pinHandle.AddrOfPinnedObject(), totalBytesRead);
                    uint remaining = (uint)(bytesToRead - totalBytesRead);

                    bool ok = ReadFile(_handle!, bufPtr, remaining, out uint bytesRead, IntPtr.Zero);
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new IOException(
                            $"ReadFile failed at sector {startSector + (totalBytesRead / (int)SectorSize)}: Win32 error {error}");
                    }

                    if (bytesRead == 0)
                        break; // Reached end of device

                    totalBytesRead += (int)bytesRead;
                }

                return totalBytesRead;
            }
            finally
            {
                pinHandle.Free();
            }
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
