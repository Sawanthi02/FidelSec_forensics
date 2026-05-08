using FidelSec.Core.Models;

namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// Contract for the forensic imaging engine.
    /// Implementations must guarantee read-only access to the source device.
    /// </summary>
    public interface IImagingEngine
    {
        /// <summary>
        /// Starts an imaging job asynchronously.
        /// Reports progress via the provided IProgress callback.
        /// </summary>
        /// <param name="job">Job configuration including source device and output options</param>
        /// <param name="progress">Callback receiving real-time progress updates</param>
        /// <param name="cancellationToken">Token to cancel or stop the operation</param>
        Task<ImagingResult> StartImagingAsync(
            ImagingJob job,
            IProgress<ImagingProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies an existing image file against provided hashes.
        /// </summary>
        Task<ImagingResult> VerifyImageAsync(
            string imagePath,
            Dictionary<HashAlgorithmType, string> expectedHashes,
            IProgress<ImagingProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>Pause a running imaging job (if supported)</summary>
        void Pause();

        /// <summary>Resume a paused imaging job</summary>
        void Resume();
    }
}
