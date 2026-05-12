using System;

namespace FidelSec.Core.Models
{
    /// <summary>
    /// Represents a physical storage device detected on the system.
    /// This model is the central entity used throughout the forensic imaging pipeline.
    /// </summary>
    public class PhysicalDevice
    {
        /// <summary>Windows device path, e.g. \\.\PhysicalDrive0</summary>
        public string DevicePath { get; set; } = string.Empty;

        /// <summary>Human-readable index, e.g. "PhysicalDrive0"</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>Drive model string from WMI (e.g. "Samsung SSD 860")</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Manufacturer serial number</summary>
        public string SerialNumber { get; set; } = string.Empty;

        /// <summary>Total capacity in bytes</summary>
        public ulong SizeBytes { get; set; }

        /// <summary>Logical sector size (usually 512 or 4096)</summary>
        public uint LogicalSectorSize { get; set; }

        /// <summary>Physical sector size (important for 4Kn drives)</summary>
        public uint PhysicalSectorSize { get; set; }

        /// <summary>Bus/interface type (SATA, NVMe, USB, etc.)</summary>
        public string InterfaceType { get; set; } = string.Empty;

        /// <summary>Media type (HDD, SSD, Removable, etc.)</summary>
        public string MediaType { get; set; } = string.Empty;

        /// <summary>Partition table type: MBR or GPT</summary>
        public PartitionStyle PartitionStyle { get; set; }

        /// <summary>List of partitions on this device</summary>
        public List<PartitionInfo> Partitions { get; set; } = new();

        /// <summary>Whether device is hardware write-protected</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>Whether device is removable (USB, SD)</summary>
        public bool IsRemovable { get; set; }

        /// <summary>Firmware revision string from WMI / hdparm</summary>
        public string FirmwareRevision { get; set; } = string.Empty;

        // ── Cross-platform fields ───────────────────────────────────────────

        /// <summary>
        /// Linux: mount points from /proc/mounts (e.g. ["/", "/boot"]).
        /// Windows: drive letters (e.g. ["C:\\", "D:\\"]).
        /// </summary>
        public List<string> MountPoints { get; set; } = new();

        /// <summary>True when at least one partition is currently mounted read-write.</summary>
        public bool IsMounted { get; set; }

        /// <summary>
        /// Linux: device is mounted read-only (ro) according to /proc/mounts.
        /// Windows: determined via IOCTL_DISK_IS_WRITABLE.
        /// </summary>
        public bool IsMountedReadOnly { get; set; }

        /// <summary>
        /// Linux: true when a hardware write-blocker is detected
        /// (sd-protect or hdparm -r reports write-protect).
        /// Windows: IsReadOnly from WMI.
        /// </summary>
        public bool HasWriteBlocker { get; set; }

        /// <summary>
        /// Windows-only: true when the drive (or a partition) is BitLocker-encrypted.
        /// </summary>
        public bool IsBitLockerEncrypted { get; set; }

        /// <summary>Source OS that populated this record (for logging).</summary>
        public string SourcePlatform { get; set; } = string.Empty;

        /// <summary>Human-readable size string (e.g. "500 GB")</summary>
        public string SizeHuman => FormatSize(SizeBytes);

        private static string FormatSize(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:F1} {units[unit]}";
        }
    }

    /// <summary>Partition style of a disk</summary>
    public enum PartitionStyle
    {
        Unknown,
        MBR,
        GPT,
        Raw
    }

    /// <summary>
    /// Represents a single partition on a physical device.
    /// </summary>
    public class PartitionInfo
    {
        public int PartitionNumber { get; set; }
        public ulong StartingOffset { get; set; }
        public ulong SizeBytes { get; set; }
        public string TypeGuid { get; set; } = string.Empty;
        public string PartitionType { get; set; } = string.Empty;
        public bool IsBootable { get; set; }
        public string DriveLetter { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string SizeHuman => FormatSize(SizeBytes);

        private static string FormatSize(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:F1} {units[unit]}";
        }
    }
}
