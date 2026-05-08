using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.DeviceDetection
{
    /// <summary>
    /// Enumerates physical disk drives using Windows Management Instrumentation (WMI).
    /// Queries Win32_DiskDrive, Win32_DiskPartition, and Win32_LogicalDisk classes
    /// to build a complete device model including partitions and file system info.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WmiDeviceScanner : IDeviceScanner
    {
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

            try
            {
                // Query all physical disk drives via WMI
                using var searcher = new ManagementObjectSearcher(
                    @"SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject disk in searcher.Get())
                {
                    try
                    {
                        var device = BuildDeviceFromWmi(disk);
                        device.Partitions = GetPartitions(disk);
                        devices.Add(device);

                        _logger.LogInformation(
                            "Discovered device: {DevicePath} | {Model} | {Size}",
                            device.DevicePath, device.Model, device.SizeHuman);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read WMI data for a disk drive entry");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WMI device scan failed");
                throw new InvalidOperationException("Failed to enumerate disk devices via WMI. " +
                    "Ensure the application is running with Administrator privileges.", ex);
            }

            return devices;
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
