using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows;

namespace FidelSec.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the right-side imaging progress panel.
    /// Starts/pauses/cancels imaging jobs and displays real-time statistics.
    /// </summary>
    public partial class ImagingProgressViewModel : ObservableObject
    {
        private readonly IImagingEngine _engine;
        private readonly IForensicLogger _forensicLogger;
        private readonly ILogger<ImagingProgressViewModel> _logger;

        private CancellationTokenSource? _cts;
        private ImagingJob? _currentJob;

        // ── Progress display ──────────────────────────────────────
        [ObservableProperty] private double _percentComplete;
        [ObservableProperty] private string _speedDisplay = "--";
        [ObservableProperty] private string _etaDisplay = "--";
        [ObservableProperty] private string _elapsedDisplay = "00:00:00";
        [ObservableProperty] private string _bytesDisplay = "--";
        [ObservableProperty] private long _badSectorCount;
        [ObservableProperty] private string _stateDisplay = "Idle";
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private bool _isCompleted;

        // ── Hash display ──────────────────────────────────────────
        [ObservableProperty] private string _md5Hash = "--";
        [ObservableProperty] private string _sha1Hash = "--";
        [ObservableProperty] private string _sha256Hash = "--";
        [ObservableProperty] private bool _hashesVerified;
        [ObservableProperty] private string _verificationStatus = "--";

        // ── Log entries ───────────────────────────────────────────
        public ObservableCollection<string> LogEntries { get; } = new();

        public ImagingProgressViewModel(
            IImagingEngine engine,
            IForensicLogger forensicLogger,
            ILogger<ImagingProgressViewModel> logger)
        {
            _engine = engine;
            _forensicLogger = forensicLogger;
            _logger = logger;
        }

        [RelayCommand(CanExecute = nameof(CanStartImaging))]
        public async Task StartImagingAsync(ImagingJob job)
        {
            _currentJob = job;
            _cts = new CancellationTokenSource();

            IsRunning = true;
            IsCompleted = false;
            BadSectorCount = 0;
            HashesVerified = false;
            LogEntries.Clear();

            AddLog($"Starting acquisition: {job.SourceDevice.DevicePath} → {job.OutputPath}");
            AddLog($"Case: {job.CaseNumber} | Evidence: {job.EvidenceNumber} | Examiner: {job.ExaminerName}");

            var progress = new Progress<ImagingProgress>(OnProgressUpdate);

            try
            {
                var result = await _engine.StartImagingAsync(job, progress, _cts.Token);
                OnImagingComplete(result);
            }
            catch (Exception ex)
            {
                AddLog($"ERROR: {ex.Message}");
                _logger.LogError(ex, "Imaging failed");
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        public void PauseResume()
        {
            if (IsPaused)
            {
                _engine.Resume();
                IsPaused = false;
                AddLog("Imaging resumed.");
            }
            else
            {
                _engine.Pause();
                IsPaused = true;
                AddLog("Imaging paused.");
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        public void CancelImaging()
        {
            _cts?.Cancel();
            AddLog("Cancellation requested...");
        }

        [RelayCommand(CanExecute = nameof(IsCompleted))]
        public async Task ExportLogsAsync()
        {
            if (_currentJob == null) return;

            string logDir = Path.Combine(
                Path.GetDirectoryName(_currentJob.OutputPath) ?? ".",
                "FidelSec_Logs");
            Directory.CreateDirectory(logDir);

            string baseName = $"case_{_currentJob.CaseNumber}_ev_{_currentJob.EvidenceNumber}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            await _forensicLogger.ExportJsonLogAsync(_currentJob.JobId, Path.Combine(logDir, baseName + ".json"));
            await _forensicLogger.ExportTextLogAsync(_currentJob.JobId, Path.Combine(logDir, baseName + ".txt"));

            AddLog($"Logs exported to: {logDir}");
            MessageBox.Show($"Chain of custody logs exported to:\n{logDir}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool CanStartImaging() => !IsRunning;

        private void OnProgressUpdate(ImagingProgress progress)
        {
            // WPF: marshal to UI thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                PercentComplete = progress.PercentComplete;
                SpeedDisplay = progress.SpeedHuman;
                EtaDisplay = progress.EstimatedTimeRemaining.ToString(@"hh\:mm\:ss");
                ElapsedDisplay = progress.Elapsed.ToString(@"hh\:mm\:ss");
                BytesDisplay = $"{FormatBytes(progress.BytesRead)} / {FormatBytes(progress.TotalBytes)}";
                BadSectorCount = progress.BadSectorCount;
                StateDisplay = progress.State.ToString();

                // Update hash displays
                if (progress.IntermediateHashes.TryGetValue(HashAlgorithmType.MD5, out var md5))
                    Md5Hash = md5;
                if (progress.IntermediateHashes.TryGetValue(HashAlgorithmType.SHA1, out var sha1))
                    Sha1Hash = sha1;
                if (progress.IntermediateHashes.TryGetValue(HashAlgorithmType.SHA256, out var sha256))
                    Sha256Hash = sha256;

                if (progress.State == ImagingState.Failed && !string.IsNullOrEmpty(progress.ErrorMessage))
                    AddLog($"FAILED: {progress.ErrorMessage}");
            });
        }

        private void OnImagingComplete(ImagingResult result)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsCompleted = true;
                StateDisplay = result.Success ? "Completed" : "Failed";

                if (result.SourceHashes.TryGetValue(HashAlgorithmType.MD5, out var md5))
                    Md5Hash = md5;
                if (result.SourceHashes.TryGetValue(HashAlgorithmType.SHA1, out var sha1))
                    Sha1Hash = sha1;
                if (result.SourceHashes.TryGetValue(HashAlgorithmType.SHA256, out var sha256))
                    Sha256Hash = sha256;

                HashesVerified = result.HashesVerified;
                VerificationStatus = result.HashesVerified ? "VERIFIED ✓" : (result.VerifyAfterImaging() ? "MISMATCH ✗" : "Not verified");

                AddLog($"Acquisition {(result.Success ? "COMPLETE" : "FAILED")}");
                AddLog($"Duration: {result.Duration:hh\\:mm\\:ss} | Written: {FormatBytes(result.TotalBytesWritten)}");
                AddLog($"Bad sectors: {result.BadSectorCount}");

                if (result.SourceHashes.Count > 0)
                    foreach (var (alg, hash) in result.SourceHashes)
                        AddLog($"{alg}: {hash}");

                if (result.HashesVerified)
                    AddLog("Hash verification: PASSED");
                else if (result.ImageHashes.Count > 0)
                    AddLog("Hash verification: FAILED — image integrity suspect!");
            });
        }

        private void AddLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogEntries.Add(entry);
            _logger.LogInformation("{Entry}", entry);
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:F1} {units[unit]}";
        }
    }

    // Extension helper
    internal static class ImagingResultExtensions
    {
        internal static bool VerifyAfterImaging(this ImagingResult result) =>
            result.ImageHashes.Count > 0;
    }
}
