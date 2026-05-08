using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;

namespace FidelSec.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the imaging configuration panel.
    /// Binds case metadata, output path, format selection, and hash options.
    /// </summary>
    public partial class ImagingConfigViewModel : ObservableObject
    {
        // ── Source ────────────────────────────────────────────────
        [ObservableProperty]
        private PhysicalDevice? _sourceDevice;

        // ── Output ────────────────────────────────────────────────
        [ObservableProperty]
        private string _outputPath = string.Empty;

        [ObservableProperty]
        private ImageFormat _selectedFormat = ImageFormat.Raw;

        public IReadOnlyList<ImageFormat> AvailableFormats { get; } =
            Enum.GetValues<ImageFormat>().ToList();

        [ObservableProperty]
        private bool _splitImage = false;

        [ObservableProperty]
        private int _splitSizeGb = 2;

        [ObservableProperty]
        private int _compressionLevel = 0;

        // ── Hashing ───────────────────────────────────────────────
        [ObservableProperty]
        private bool _hashMd5 = true;

        [ObservableProperty]
        private bool _hashSha1 = false;

        [ObservableProperty]
        private bool _hashSha256 = true;

        [ObservableProperty]
        private bool _verifyAfterImaging = true;

        // ── Bad sectors ───────────────────────────────────────────
        [ObservableProperty]
        private int _badSectorRetries = 3;

        [ObservableProperty]
        private bool _zeroFillBadSectors = true;

        // ── Case metadata ─────────────────────────────────────────
        [ObservableProperty]
        private string _caseNumber = string.Empty;

        [ObservableProperty]
        private string _evidenceNumber = string.Empty;

        [ObservableProperty]
        private string _examinerName = string.Empty;

        [ObservableProperty]
        private string _examinerOrganization = string.Empty;

        [ObservableProperty]
        private string _notes = string.Empty;

        [RelayCommand]
        private void BrowseOutputPath()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Select output image file",
                Filter = SelectedFormat == ImageFormat.Raw
                    ? "Raw Image (*.dd)|*.dd|All files (*.*)|*.*"
                    : "E01 Image (*.E01)|*.E01|All files (*.*)|*.*",
                DefaultExt = SelectedFormat == ImageFormat.Raw ? ".dd" : ".E01"
            };

            if (dialog.ShowDialog() == true)
                OutputPath = dialog.FileName;
        }

        /// <summary>
        /// Builds an ImagingJob from the current UI configuration.
        /// Returns null and populates validationError if required fields are missing.
        /// </summary>
        public ImagingJob? BuildJob(out string? validationError)
        {
            if (SourceDevice == null) { validationError = "No source device selected."; return null; }
            if (string.IsNullOrWhiteSpace(OutputPath)) { validationError = "Output path is required."; return null; }
            if (string.IsNullOrWhiteSpace(ExaminerName)) { validationError = "Examiner name is required."; return null; }
            if (string.IsNullOrWhiteSpace(CaseNumber)) { validationError = "Case number is required."; return null; }
            if (string.IsNullOrWhiteSpace(EvidenceNumber)) { validationError = "Evidence number is required."; return null; }

            var algorithms = new List<HashAlgorithmType>();
            if (HashMd5) algorithms.Add(HashAlgorithmType.MD5);
            if (HashSha1) algorithms.Add(HashAlgorithmType.SHA1);
            if (HashSha256) algorithms.Add(HashAlgorithmType.SHA256);
            if (algorithms.Count == 0) { validationError = "Select at least one hash algorithm."; return null; }

            validationError = null;
            return new ImagingJob
            {
                SourceDevice = SourceDevice,
                OutputPath = OutputPath,
                Format = SelectedFormat,
                SplitImage = SplitImage,
                SplitSegmentSizeBytes = (long)SplitSizeGb * 1024 * 1024 * 1024,
                CompressionLevel = CompressionLevel,
                HashAlgorithms = algorithms.ToArray(),
                VerifyAfterImaging = VerifyAfterImaging,
                BadSectorRetries = BadSectorRetries,
                ZeroFillBadSectors = ZeroFillBadSectors,
                CaseNumber = CaseNumber,
                EvidenceNumber = EvidenceNumber,
                ExaminerName = ExaminerName,
                ExaminerOrganization = ExaminerOrganization,
                Notes = Notes
            };
        }
    }
}
