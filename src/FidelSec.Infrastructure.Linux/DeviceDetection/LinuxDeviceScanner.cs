using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.Linux.DeviceDetection
{
    /// <summary>
    /// Enumerates physical block devices on Linux using `lsblk --json`.
    /// Falls back to /proc/partitions if lsblk is unavailable.
    ///
    /// Requires root or CAP_SYS_RAWIO to later open /dev/* for imaging.
    /// Device enumeration itself works as a regular user.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class LinuxDeviceScanner : IDeviceScanner
    {
        private readonly ILogger<LinuxDeviceScanner> _logger;

        public LinuxDeviceScanner(ILogger<LinuxDeviceScanner> logger)
        {
            _logger = logger;
        }

        public async Task<IReadOnlyList<PhysicalDevice>> ScanDevicesAsync(
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting Linux device scan via lsblk");

            try
            {
                return await ScanViaLsblkAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "lsblk scan failed, falling back to /proc/partitions");
                return await ScanViaProcPartitionsAsync(cancellationToken);
            }
        }

        public async Task<PhysicalDevice?> GetDeviceAsync(
            string devicePath,
            CancellationToken cancellationToken = default)
        {
            var all = await ScanDevicesAsync(cancellationToken);
            return all.FirstOrDefault(d =>
                d.DevicePath.Equals(devicePath, StringComparison.OrdinalIgnoreCase));
        }

        // ── lsblk implementation ──────────────────────────────────────────

        private async Task<List<PhysicalDevice>> ScanViaLsblkAsync(
            CancellationToken ct)
        {
            // lsblk -d = disk-only (no partitions in top-level output)
            // -o = fields, -b = size in bytes, --json = JSON output
            const string args =
                "--json -d -b -o NAME,TYPE,SIZE,MODEL,SERIAL,TRAN,ROTA,RM,RO,MOUNTPOINTS,PHY-SEC,LOG-SEC";

            string json = await RunCommandAsync("lsblk", args, ct);
            return ParseLsblkJson(json);
        }

        private List<PhysicalDevice> ParseLsblkJson(string json)
        {
            var devices = new List<PhysicalDevice>();

            var root = JsonNode.Parse(json);
            var blockDevices = root?["blockdevices"]?.AsArray();
            if (blockDevices == null) return devices;

            foreach (var node in blockDevices)
            {
                if (node == null) continue;
                string type = node["type"]?.GetValue<string>() ?? "";
                if (type != "disk") continue;

                string name   = node["name"]?.GetValue<string>() ?? "";
                string path   = $"/dev/{name}";

                // Parse mount points (may be array or null)
                var mountArray = node["mountpoints"]?.AsArray();
                var mounts = mountArray?
                    .Select(m => m?.GetValue<string>() ?? "")
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToList() ?? new List<string>();

                bool isReadOnly = node["ro"]?.GetValue<bool>() ?? false;
                bool isRemovable = node["rm"]?.GetValue<bool>() ?? false;

                ulong sizeBytes = 0;
                if (node["size"] != null)
                    ulong.TryParse(node["size"]?.ToString(), out sizeBytes);

                uint logSector = 512;
                if (node["log-sec"] != null)
                    uint.TryParse(node["log-sec"]?.ToString(), out logSector);

                uint phySector = 512;
                if (node["phy-sec"] != null)
                    uint.TryParse(node["phy-sec"]?.ToString(), out phySector);

                var device = new PhysicalDevice
                {
                    DevicePath          = path,
                    DeviceId            = name,
                    Model               = node["model"]?.GetValue<string>() ?? "",
                    SerialNumber        = node["serial"]?.GetValue<string>() ?? "",
                    SizeBytes           = sizeBytes,
                    LogicalSectorSize   = logSector,
                    PhysicalSectorSize  = phySector,
                    InterfaceType       = MapTransport(node["tran"]?.GetValue<string>()),
                    MediaType           = (node["rota"]?.GetValue<bool>() ?? true) ? "HDD" : "SSD",
                    IsReadOnly          = isReadOnly,
                    IsRemovable         = isRemovable,
                    MountPoints         = mounts,
                    IsMounted           = mounts.Count > 0,
                    SourcePlatform      = "Linux"
                };

                // Determine partition style from /sys/block
                device.PartitionStyle = DetectPartitionStyle(name);

                // Warn if device is mounted writable (contamination risk)
                device.IsMountedReadOnly = isReadOnly && mounts.Count > 0;

                // Check for write-blocker (hdparm -r)
                device.HasWriteBlocker = CheckWriteBlocker(path);

                // Get full partition info
                device.Partitions = GetPartitions(name);

                _logger.LogInformation(
                    "Discovered: {Path} | {Model} | {Size} | Mounted={Mounted}",
                    path, device.Model, device.SizeHuman, device.IsMounted);

                devices.Add(device);
            }

            return devices;
        }

        // ── /proc/partitions fallback ─────────────────────────────────────

        private async Task<List<PhysicalDevice>> ScanViaProcPartitionsAsync(
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync("/proc/partitions", ct);
            var devices = new List<PhysicalDevice>();

            foreach (var line in lines.Skip(2)) // skip header lines
            {
                var parts = line.Trim().Split(' ',
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                string name = parts[3];
                // Only top-level disks (sda, nvme0n1, etc.) — skip partitions
                if (name.Length < 3) continue;
                bool isPartition = (name.StartsWith("sd") && char.IsDigit(name[^1]))
                                || name.Contains("p") && char.IsDigit(name[^1]);
                if (isPartition) continue;

                string path = $"/dev/{name}";
                if (!File.Exists(path)) continue;

                ulong.TryParse(parts[2], out ulong sizeKb);

                devices.Add(new PhysicalDevice
                {
                    DevicePath         = path,
                    DeviceId           = name,
                    Model              = ReadSysBlock(name, "device/model"),
                    SerialNumber       = ReadSysBlock(name, "device/serial"),
                    SizeBytes          = sizeKb * 1024,
                    LogicalSectorSize  = 512,
                    PhysicalSectorSize = 512,
                    SourcePlatform     = "Linux",
                    PartitionStyle     = DetectPartitionStyle(name),
                    Partitions         = GetPartitions(name)
                });
            }

            return devices;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static string MapTransport(string? tran)
        {
            return tran?.ToUpperInvariant() switch
            {
                "SATA"  => "SATA",
                "USB"   => "USB",
                "NVME"  => "NVMe",
                "SAS"   => "SAS",
                "SCSI"  => "SCSI",
                "MMC"   => "MMC/SD",
                _       => tran ?? "Unknown"
            };
        }

        private static PartitionStyle DetectPartitionStyle(string name)
        {
            // Check /sys/block/<name>/queue/logical_block_size and GPT signature at LBA1
            // Simple heuristic via /sys/block/<name>/device/*
            string ptType = ReadSysBlock(name, "../../partition-table-type");
            if (string.IsNullOrEmpty(ptType))
            {
                // Try blkid if available
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("blkid",
                        $"-p -o value -s PTTYPE /dev/{name}")
                    { RedirectStandardOutput = true, UseShellExecute = false });
                    string? result = p?.StandardOutput.ReadLine()?.Trim();
                    ptType = result ?? "";
                }
                catch { }
            }

            return ptType.ToLowerInvariant() switch
            {
                "gpt" => PartitionStyle.GPT,
                "dos" or "mbr" => PartitionStyle.MBR,
                _ => PartitionStyle.Unknown
            };
        }

        private static bool CheckWriteBlocker(string devicePath)
        {
            // Check read-only flag via sysfs
            string name = Path.GetFileName(devicePath);
            string roPath = $"/sys/block/{name}/ro";
            if (File.Exists(roPath) && File.ReadAllText(roPath).Trim() == "1")
                return true;

            // Try hdparm -r if available
            try
            {
                using var p = Process.Start(new ProcessStartInfo("hdparm", $"-r {devicePath}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });
                string? output = p?.StandardOutput.ReadToEnd();
                if (output != null && output.Contains(" 1"))
                    return true;
            }
            catch { /* hdparm not available */ }

            return false;
        }

        private List<PartitionInfo> GetPartitions(string diskName)
        {
            var partitions = new List<PartitionInfo>();
            string sysPath = $"/sys/block/{diskName}";
            if (!Directory.Exists(sysPath)) return partitions;

            int partNum = 0;
            foreach (var dir in Directory.GetDirectories(sysPath))
            {
                string partName = Path.GetFileName(dir);
                if (!partName.StartsWith(diskName)) continue;

                ulong.TryParse(ReadSysFile($"{dir}/size"), out ulong sectors);
                ulong.TryParse(ReadSysFile($"{dir}/start"), out ulong startSector);
                ulong sizeBytes = sectors * 512;

                // Get filesystem type via blkid
                string fsType = "";
                string fsLabel = "";
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("blkid",
                        $"-o export /dev/{partName}")
                    { RedirectStandardOutput = true, UseShellExecute = false });
                    string? output = p?.StandardOutput.ReadToEnd();
                    if (output != null)
                    {
                        foreach (var line in output.Split('\n'))
                        {
                            if (line.StartsWith("TYPE="))  fsType  = line[5..].Trim();
                            if (line.StartsWith("LABEL=")) fsLabel = line[6..].Trim();
                        }
                    }
                }
                catch { }

                partitions.Add(new PartitionInfo
                {
                    PartitionNumber = ++partNum,
                    StartingOffset  = startSector * 512,
                    SizeBytes       = sizeBytes,
                    FileSystem      = fsType,
                    Label           = fsLabel,
                    PartitionType   = ""
                });
            }

            return partitions;
        }

        private static string ReadSysBlock(string name, string subPath)
            => ReadSysFile($"/sys/block/{name}/{subPath}");

        private static string ReadSysFile(string path)
        {
            try { return File.ReadAllText(path).Trim(); }
            catch { return string.Empty; }
        }

        private static async Task<string> RunCommandAsync(
            string command, string arguments, CancellationToken ct)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName               = command,
                Arguments              = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return output;
        }
    }
}
