using FidelSec.Core.Models;

namespace FidelSec.Core.Interfaces
{
    /// <summary>
    /// Contract for computing cryptographic hashes.
    /// Supports streaming (on-the-fly) and batch modes.
    /// </summary>
    public interface IHashingEngine
    {
        /// <summary>
        /// Computes hashes of an entire file from disk.
        /// </summary>
        Task<Dictionary<HashAlgorithmType, string>> ComputeFileHashesAsync(
            string filePath,
            HashAlgorithmType[] algorithms,
            IProgress<long>? bytesProcessed = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Initializes streaming hasher for on-the-fly computation during imaging.
        /// Call FeedData() for each buffer, then Finalize() at the end.
        /// </summary>
        IStreamingHasher CreateStreamingHasher(HashAlgorithmType[] algorithms);
    }

    /// <summary>
    /// Streaming hash accumulator that processes data incrementally.
    /// Thread-safe — can be called from the imaging IO thread.
    /// </summary>
    public interface IStreamingHasher : IDisposable
    {
        /// <summary>Feed a buffer of data into all active hash accumulators</summary>
        void FeedData(byte[] buffer, int offset, int count);

        /// <summary>Feed a span of data (zero-copy path)</summary>
        void FeedData(ReadOnlySpan<byte> data);

        /// <summary>
        /// Finalizes all accumulators and returns hex-encoded digests.
        /// Must be called exactly once after all data has been fed.
        /// </summary>
        Dictionary<HashAlgorithmType, string> Finalize();

        /// <summary>
        /// Returns intermediate (non-final) hex digests for display purposes.
        /// Does NOT finalize the hashers.
        /// </summary>
        Dictionary<HashAlgorithmType, string> GetIntermediateValues();
    }
}
