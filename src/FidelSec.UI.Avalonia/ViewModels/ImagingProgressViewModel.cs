using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace FidelSec.UI.Avalonia.ViewModels
{
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
        [ObservableProperty] private string _etaDisplay   = "--";
        [ObservableProperty] private string _elapsedDisplay = "00:00:00";
        [ObservableProperty] private string _bytesDisplay  = "--";
        [ObservableProperty] private long   _badSectorCount;
        [ObservableProperty] private string _stateDisplay = "Inactief";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PauseResumeCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelImagingCommand))]
        private bool _isRunning;

        [ObservableProperty] private bool _isPaused;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportLogsCommand))]
        private bool _isCompleted;

        // ── Hash display ──────────────────────────────────────────
        [ObservableProperty] private string _md5Hash           = "--";
        [ObservableProperty] private string _sha1Hash          = "--";
        [ObservableProperty] private string _sha256Hash        = "--";
        [ObservableProperty] private bool   _hashesVerified;
        [ObservableProperty] private string _verificationStatus = "--";

        // ── Log entries ───────────────────────────────────────────
        public ObservableCollection<string> LogEntries { get; } = new();

        public ImagingProgressViewModel(
            IImagingEngine engine,
            IForensicLogger forensicLogger,
            ILogger<ImagingProgressViewModel> logger)
        {
            _engine         = engine;
            _forensicLogger = forensicLogger;
            _logger         = logger;
        }

        [RelayCommand(CanExecute = nameof(CanStartImaging))]
        public async Task StartImagingAsync(ImagingJob job)
        {
            _currentJob = job;
            _cts        = new CancellationTokenSource();

            IsRunning   = true;
            IsCompleted = false;
            BadSectorCount = 0;
            HashesVerified = false;
            LogEntries.Clear();

            AddLog($"Acquisitie gestart: {job.SourceDevice.DevicePath} -> {job.OutputPath}");
            AddLog($"Zaak: {job.CaseNumber} | Bewijsnummer: {job.EvidenceNumber} | Onderzoeker: {job.ExaminerName}");

            var progress = new Progress<ImagingProgress>(OnProgressUpdate);

            try
            {
                var result = await _engine.StartImagingAsync(job, progress, _cts.Token);
                OnImagingComplete(result);
            }
            catch (OperationCanceledException)
            {
                AddLog("Acquisitie geannuleerd.");
                StateDisplay = "Geannuleerd";
            }
            catch (Exception ex)
            {
                AddLog($"FOUT: {ex.Message}");
                _logger.LogError(ex, "Imaging failed");
                StateDisplay = "Fout";
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
                AddLog("Acquisitie hervat.");
            }
            else
            {
                _engine.Pause();
                IsPaused = true;
                AddLog("Acquisitie gepauzeerd.");
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        public void CancelImaging()
        {
            _cts?.Cancel();
            AddLog("Annuleren aangevraagd...");
        }

        [RelayCommand(CanExecute = nameof(IsCompleted))]
        public async Task ExportLogsAsync()
        {
            if (_currentJob is null) return;

            try
            {
                string outputDir = System.IO.Path.GetDirectoryName(_currentJob.OutputPath)
                    ?? System.IO.Directory.GetCurrentDirectory();
                string logDir = System.IO.Path.Combine(outputDir, "FidelSec_Logs");
                System.IO.Directory.CreateDirectory(logDir);

                string baseName = $"case_{Sanitize(_currentJob.CaseNumber)}_ev_{Sanitize(_currentJob.EvidenceNumber)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                string jsonPath = System.IO.Path.Combine(logDir, baseName + ".json");
                string txtPath  = System.IO.Path.Combine(logDir, baseName + ".txt");

                await _forensicLogger.ExportJsonLogAsync(_currentJob.JobId, jsonPath);
                await _forensicLogger.ExportTextLogAsync(_currentJob.JobId, txtPath);

                AddLog($"Chain-of-custody log opgeslagen:");
                AddLog($"  TXT: {txtPath}");
                AddLog($"  JSON: {jsonPath}");
            }
            catch (Exception ex)
            {
                AddLog($"FOUT bij exporteren van logs: {ex.Message}");
                _logger.LogError(ex, "Log export failed");
            }
        }

        private static string Sanitize(string s) =>
            string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));

        private bool CanStartImaging() => !IsRunning;

        // Avalonia: progress callback already on correct thread via Dispatcher
        // but to be safe we ensure UI-thread access
        private void OnProgressUpdate(ImagingProgress progress)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                PercentComplete = progress.PercentComplete;
                SpeedDisplay    = progress.SpeedHuman;
                EtaDisplay      = progress.EstimatedTimeRemaining.ToString(@"hh\:mm\:ss");
                ElapsedDisplay  = progress.Elapsed.ToString(@"hh\:mm\:ss");
                BytesDisplay    = $"{FormatBytes(progress.BytesRead)} / {FormatBytes(progress.TotalBytes)}";
                BadSectorCount  = progress.BadSectorCount;
                StateDisplay    = progress.State.ToString();

                if (progress.IntermediateHashes.TryGetValue(HashAlgorithmType.MD5,    out var md5))    Md5Hash    = md5;
                if (progress.IntermediateHashes.TryGetValue(HashAlgorithmType.SHA1,   out var sha1))   Sha1Hash   = sha1;
                if (progress.IntermediateHashes.TryGetValue(HashAlgorithmType.SHA256, out var sha256)) Sha256Hash = sha256;

                if (progress.State == ImagingState.Failed && !string.IsNullOrEmpty(progress.ErrorMessage))
                    AddLog($"MISLUKT: {progress.ErrorMessage}");
            });
        }

        private void OnImagingComplete(ImagingResult result)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsCompleted  = true;
                StateDisplay = result.Success ? "Voltooid" : "Mislukt";

                if (result.SourceHashes.TryGetValue(HashAlgorithmType.MD5,    out var md5))    Md5Hash    = md5;
                if (result.SourceHashes.TryGetValue(HashAlgorithmType.SHA1,   out var sha1))   Sha1Hash   = sha1;
                if (result.SourceHashes.TryGetValue(HashAlgorithmType.SHA256, out var sha256)) Sha256Hash = sha256;

                HashesVerified     = result.HashesVerified;
                VerificationStatus = result.HashesVerified ? "GEVERIFIEERD" : (result.ImageHashes.Count > 0 ? "MISMATCH" : "Niet geverifieerd");

                AddLog($"Acquisitie {(result.Success ? "VOLTOOID" : "MISLUKT")}");
                AddLog($"Duur: {result.Duration:hh\\:mm\\:ss} | Geschreven: {FormatBytes(result.TotalBytesWritten)}");
                AddLog($"Slechte sectoren: {result.BadSectorCount}");
                foreach (var (alg, hash) in result.SourceHashes)
                    AddLog($"{alg}: {hash}");
                if (result.HashesVerified)
                    AddLog("Hash-verificatie: GESLAAGD");
                else if (result.ImageHashes.Count > 0)
                    AddLog("Hash-verificatie: MISLUKT — image-integriteit verdacht!");
            });
        }

        private void AddLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => LogEntries.Add(entry));
            _logger.LogInformation("{Entry}", entry);
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1) { val /= 1024; idx++; }
            return $"{val:F1} {units[idx]}";
        }
    }
}
