using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.DeviceDetection
{
    /// <summary>
    /// Enumerates physical disk drives.
    /// Primary: WMI Win32_DiskDrive (rich metadata).
    /// Fallback: brute-force \\.\PhysicalDriveN enumeration via Win32 API.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WmiDeviceScanner : IDeviceScanner
    {
        // ── Win32 for fallback enumeration ───────────────────────────────────
        private const uint GENERIC_READ  = 0x80000000;
        private const uint FILE_SHARE_READ  = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSec, uint dwCreationDisp, uint dwFlagsAndAttr, IntPtr hTemplate);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct DISK_GEOMETRY { public long Cylinders; public uint MediaType; public uint TracksPerCylinder; public uint SectorsPerTrack; public uint BytesPerSector; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DISK_GEOMETRY_EX { public DISK_GEOMETRY Geometry; public long DiskSize; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public byte[] Data; }

        private readonly ILogger<WmiDeviceScanner> _logger;

        public WmiDeviceScanner(ILogger<WmiDeviceScanner> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PhysicalDevice>> ScanDevicesAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => ScanDevices(), cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<PhysicalDevice?> GetDeviceAsync(string devicePath, CancellationToken cancellationToken = default)
        {
            var all = await ScanDevicesAsync(cancellationToken);
            return all.FirstOrDefault(d => d.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase));
        }

        private List<PhysicalDevice> ScanDevices()
        {
            var devices = new List<PhysicalDevice>();

            // ── Primary: WMI ──────────────────────────────────────────────────
            bool wmiSucceeded = false;
            try
            {
                using var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject disk in searcher.Get())
                {
                    try
                    {
                        var device = BuildDeviceFromWmi(disk);
                        device.Partitions = GetPartitions(disk);
                        devices.Add(device);
                        _logger.LogInformation("WMI: {Path} | {Model} | {Size}",
                            device.DevicePath, device.Model, device.SizeHuman);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read WMI data for one disk");
                    }
                }
                wmiSucceeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WMI scan failed — will use brute-force fallback");
            }

            // ── Fallback: brute-force PhysicalDrive0..15 ─────────────────────
            // Used when WMI returned 0 results or threw (e.g. WMI service unavailable).
            if (!wmiSucceeded || devices.Count == 0)
            {
                _logger.LogInformation("Running brute-force PhysicalDrive enumeration");
                for (int i = 0; i <= 15; i++)
                {
                    string path = $@"\\.\PhysicalDrive{i}";
                    var device = TryOpenPhysicalDrive(path, i);
                    if (device is not null)
                    {
                        devices.Add(device);
                        _logger.LogInformation("Fallback found: {Path} | {Size}", path, device.SizeHuman);
                    }
                }
            }

            // ── Supplement: removable drives via DriveInfo ────────────────────
            // Catches USB sticks / card readers that may not appear in WMI above.
            try
            {
                foreach (var di in DriveInfo.GetDrives())
                {
                    if (di.DriveType != DriveType.Removable && di.DriveType != DriveType.Fixed)
                        continue;

                    string logicalPath = di.Name.TrimEnd('\\', '/'); // e.g. "D:"
                    // Skip if already covered by a WMI/fallback device
                    bool alreadyCovered = devices.Any(d =>
                        d.Partitions.Any(p =>
                            p.DriveLetter.Equals(logicalPath, StringComparison.OrdinalIgnoreCase)));

                    if (!alreadyCovered && di.DriveType == DriveType.Removable)
                    {
                        ulong size = 0;
                        try { size = (ulong)di.TotalSize; } catch { }
                        devices.Add(new PhysicalDevice
                        {
                            DevicePath = logicalPath,
                            DeviceId  = logicalPath,
                            Model     = di.IsReady && !string.IsNullOrEmpty(di.VolumeLabel)
                                            ? di.VolumeLabel
                                            : $"Verwisselbaar ({logicalPath})",
                            InterfaceType = "USB/Removable",
                            MediaType     = "Removable Media",
                            SizeBytes     = size,
                            IsRemovable   = true,
                            SourcePlatform = "DriveInfo",
                        });
                        _logger.LogInformation("DriveInfo removable: {Path}", logicalPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DriveInfo supplemental scan failed (non-fatal)");
            }

            if (devices.Count == 0)
                throw new InvalidOperationException(
                    "Geen apparaten gevonden. Controleer of de applicatie als Administrator wordt uitgevoerd.");

            return devices;
        }

        /// <summary>
        /// Brute-force fallback: opens a raw PhysicalDrive handle and reads geometry via IOCTL.
        /// Returns null if the drive does not exist (error 2 = file not found).
        /// </summary>
        private PhysicalDevice? TryOpenPhysicalDrive(string path, int index)
        {
            var handle = CreateFile(
                path, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle.IsInvalid)
                return null;   // drive does not exist at this index

            try
            {
                int structSize = Marshal.SizeOf<DISK_GEOMETRY_EX>() + 64;
                var buf = Marshal.AllocHGlobal(structSize);
                try
                {
                    bool ok = DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                        IntPtr.Zero, 0, buf, (uint)structSize, out _, IntPtr.Zero);

                    ulong sizeBytes = 0;
                    uint bytesPerSector = 512;
                    if (ok)
                    {
                        var geo = Marshal.PtrToStructure<DISK_GEOMETRY_EX>(buf);
                        sizeBytes = (ulong)geo.DiskSize;
                        bytesPerSector = geo.Geometry.BytesPerSector;
                    }

                    return new PhysicalDevice
                    {
                        DevicePath         = path,
                        DeviceId           = $"PhysicalDrive{index}",
                        Model              = $"Schijf {index}",
                        SerialNumber       = "Onbekend",
                        SizeBytes          = sizeBytes,
                        LogicalSectorSize  = bytesPerSector,
                        PhysicalSectorSize = bytesPerSector,
                        InterfaceType      = "Unknown",
                        MediaType          = "Unknown",
                        PartitionStyle     = PartitionStyle.Unknown,
                        FirmwareRevision   = "Unknown",
                        SourcePlatform     = "Win32API",
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IOCTL failed for {Path}", path);
                return null;
            }
            finally
            {
                handle.Dispose();
            }
        }

        private static PhysicalDevice BuildDeviceFromWmi(ManagementObject disk)
        {
            string? deviceId = disk["DeviceID"]?.ToString();  // e.g. \\.\PHYSICALDRIVE0
            string? model = disk["Model"]?.ToString()?.Trim();
            string? serial = disk["SerialNumber"]?.ToString()?.Trim();
            ulong sizeBytes = disk["Size"] != null ? Convert.ToUInt64(disk["Size"]) : 0;
            uint bytesPerSector = disk["BytesPerSector"] != null ? Convert.ToUInt32(disk["BytesPerSector"]) : 512;
            string? interfaceType = disk["InterfaceType"]?.ToString();
            string? mediaType = disk["MediaType"]?.ToString();
            string? firmware = disk["FirmwareRevision"]?.ToString()?.Trim();
            uint partitionsCount = disk["Partitions"] != null ? Convert.ToUInt32(disk["Partitions"]) : 0;

            // Determine partition style by reading the first 512 bytes (MBR signature check)
            // We set Unknown here; the ImagingEngine will refine it on open
            var partStyle = DeterminePartitionStyleFromWmi(disk);

            return new PhysicalDevice
            {
                DevicePath = deviceId ?? string.Empty,
                DeviceId = ExtractDriveId(deviceId),
                Model = model ?? "Unknown",
                SerialNumber = serial ?? "Unknown",
                SizeBytes = sizeBytes,
                LogicalSectorSize = bytesPerSector,
                PhysicalSectorSize = bytesPerSector, // WMI does not always expose physical sector size
                InterfaceType = interfaceType ?? "Unknown",
                MediaType = mediaType ?? "Unknown",
                PartitionStyle = partStyle,
                FirmwareRevision = firmware ?? "Unknown",
                IsReadOnly = false, // determined by IOCTL in DiskReader
                IsRemovable = mediaType?.Contains("Removable", StringComparison.OrdinalIgnoreCase) ?? false
            };
        }

        private static PartitionStyle DeterminePartitionStyleFromWmi(ManagementObject disk)
        {
            // WMI does not directly expose GPT/MBR; we infer from partition info
            // A more accurate check is done via IOCTL_DISK_GET_DRIVE_LAYOUT_EX in the DiskReader
            try
            {
                using var partSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{EscapeWmiPath(disk["DeviceID"]?.ToString())}'}} " +
                    $"WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject partition in partSearcher.Get())
                {
                    string? type = partition["Type"]?.ToString();
                    if (type != null && type.Contains("GPT", StringComparison.OrdinalIgnoreCase))
                        return PartitionStyle.GPT;
                }

                // If we got partitions but none say GPT, assume MBR
                return PartitionStyle.MBR;
            }
            catch
            {
                return PartitionStyle.Unknown;
            }
        }

        private static List<PartitionInfo> GetPartitions(ManagementObject disk)
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                using var partSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{EscapeWmiPath(disk["DeviceID"]?.ToString())}'}} " +
                    $"WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject part in partSearcher.Get())
                {
                    var pi = new PartitionInfo
                    {
                        PartitionNumber = Convert.ToInt32(part["Index"]),
                        StartingOffset = Convert.ToUInt64(part["StartingOffset"]),
                        SizeBytes = Convert.ToUInt64(part["Size"]),
                        IsBootable = part["Bootable"] != null && Convert.ToBoolean(part["Bootable"]),
                        PartitionType = part["Type"]?.ToString() ?? "Unknown"
                    };

                    // Get logical disk info (drive letter, filesystem)
                    using var logicalSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} " +
                        $"WHERE AssocClass=Win32_LogicalDiskToPartition");

                    foreach (ManagementObject logical in logicalSearcher.Get())
                    {
                        pi.DriveLetter = logical["DeviceID"]?.ToString() ?? string.Empty;
                        pi.FileSystem = logical["FileSystem"]?.ToString() ?? string.Empty;
                        pi.Label = logical["VolumeName"]?.ToString() ?? string.Empty;
                        break; // Each partition maps to at most one logical disk
                    }

                    partitions.Add(pi);
                }
            }
            catch (Exception)
            {
                // Non-fatal: return what we have
            }

            partitions.Sort((a, b) => a.PartitionNumber.CompareTo(b.PartitionNumber));
            return partitions;
        }

        private static string ExtractDriveId(string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return "Unknown";
            // \\.\PHYSICALDRIVE0 → PhysicalDrive0
            return deviceId.Replace(@"\\.\", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeWmiPath(string? path)
        {
            // WMI queries require backslashes to be doubled in string literals
            return path?.Replace(@"\", @"\\") ?? string.Empty;
        }
    }
}
