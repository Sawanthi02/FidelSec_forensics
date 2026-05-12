using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Models;

namespace FidelSec.UI.Avalonia.ViewModels
{
    public partial class ImagingConfigViewModel : ObservableObject
    {
        private Window? _ownerWindow;

        // ── Source ────────────────────────────────────────────────
        [ObservableProperty] private PhysicalDevice? _sourceDevice;

        // ── Output ────────────────────────────────────────────────
        [ObservableProperty] private string _outputPath = string.Empty;
        [ObservableProperty] private ImageFormat _selectedFormat = ImageFormat.Raw;

        public IReadOnlyList<ImageFormat> AvailableFormats { get; } =
            Enum.GetValues<ImageFormat>().ToList();

        [ObservableProperty] private bool _splitImage;
        [ObservableProperty] private int  _splitSizeGb   = 2;
        [ObservableProperty] private int  _compressionLevel = 0;

        // ── Hashing ───────────────────────────────────────────────
        [ObservableProperty] private bool _hashMd5    = true;
        [ObservableProperty] private bool _hashSha1   = false;
        [ObservableProperty] private bool _hashSha256 = true;
        [ObservableProperty] private bool _verifyAfterImaging = true;

        // ── Bad sectors ───────────────────────────────────────────
        [ObservableProperty] private int  _badSectorRetries  = 3;
        [ObservableProperty] private bool _zeroFillBadSectors = true;

        // ── Case metadata ─────────────────────────────────────────
        [ObservableProperty] private string _caseNumber             = string.Empty;
        [ObservableProperty] private string _evidenceNumber         = string.Empty;
        [ObservableProperty] private string _examinerName           = string.Empty;
        [ObservableProperty] private string _examinerOrganization   = string.Empty;
        [ObservableProperty] private string _notes                  = string.Empty;

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        [RelayCommand]
        private async Task BrowseOutputPathAsync()
        {
            if (_ownerWindow is null) return;

            string ext  = SelectedFormat == ImageFormat.Raw ? ".dd" : ".E01";
            string desc = SelectedFormat == ImageFormat.Raw
                ? "Raw disk image (*.dd)"
                : "E01 disk image (*.E01)";

            var options = new FilePickerSaveOptions
            {
                Title               = "Sla image op als",
                SuggestedFileName   = $"image{ext}",
                DefaultExtension    = ext,
                FileTypeChoices     = new[]
                {
                    new FilePickerFileType(desc)
                    {
                        Patterns = new[] { $"*{ext}" }
                    },
                    FilePickerFileTypes.All
                }
            };

            var result = await _ownerWindow.StorageProvider.SaveFilePickerAsync(options);
            if (result is not null)
                OutputPath = result.Path.LocalPath;
        }

        /// <summary>
        /// Builds the ImagingJob from current settings.
        /// </summary>
        public ImagingJob? BuildJob()
        {
            if (SourceDevice is null || string.IsNullOrWhiteSpace(OutputPath))
                return null;

            var algorithms = new List<HashAlgorithmType>();
            if (HashMd5)    algorithms.Add(HashAlgorithmType.MD5);
            if (HashSha1)   algorithms.Add(HashAlgorithmType.SHA1);
            if (HashSha256) algorithms.Add(HashAlgorithmType.SHA256);

            return new ImagingJob
            {
                JobId                 = Guid.NewGuid(),
                SourceDevice          = SourceDevice,
                OutputPath            = OutputPath,
                Format                = SelectedFormat,
                SplitImage            = SplitImage,
                SplitSegmentSizeBytes = (long)SplitSizeGb * 1024 * 1024 * 1024,
                CompressionLevel      = CompressionLevel,
                HashAlgorithms        = algorithms.ToArray(),
                VerifyAfterImaging    = VerifyAfterImaging,
                BadSectorRetries      = BadSectorRetries,
                ZeroFillBadSectors    = ZeroFillBadSectors,
                CaseNumber            = CaseNumber,
                EvidenceNumber        = EvidenceNumber,
                ExaminerName          = ExaminerName,
                ExaminerOrganization  = ExaminerOrganization,
                Notes                 = Notes
            };
        }
    }
}
