using System.Security.Cryptography;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Models;
using Microsoft.Extensions.Logging;

namespace FidelSec.Infrastructure.Hashing
{
    /// <summary>
    /// Cryptographic hashing engine supporting MD5, SHA-1, and SHA-256.
    /// Uses .NET's managed implementations — no external dependencies.
    ///
    /// For on-the-fly imaging, use CreateStreamingHasher() which runs
    /// all requested algorithms in parallel via separate HashAlgorithm instances.
    /// </summary>
    public class HashingEngine : IHashingEngine
    {
        private readonly ILogger<HashingEngine> _logger;

        public HashingEngine(ILogger<HashingEngine> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<HashAlgorithmType, string>> ComputeFileHashesAsync(
            string filePath,
            HashAlgorithmType[] algorithms,
            IProgress<long>? bytesProcessed = null,
            CancellationToken cancellationToken = default)
        {
            using var hasher = CreateStreamingHasher(algorithms);

            const int bufferSize = 4 * 1024 * 1024; // 4 MB read buffer
            byte[] buffer = new byte[bufferSize];
            long totalRead = 0;

            await using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                useAsync: true);

            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                hasher.FeedData(buffer, 0, bytesRead);
                totalRead += bytesRead;
                bytesProcessed?.Report(totalRead);
            }

            var results = hasher.Finalize();
            _logger.LogInformation("File hash complete for {Path}: {Hashes}",
                filePath, string.Join(", ", results.Select(kv => $"{kv.Key}={kv.Value}")));

            return results;
        }

        /// <inheritdoc/>
        public IStreamingHasher CreateStreamingHasher(HashAlgorithmType[] algorithms)
        {
            return new StreamingHasher(algorithms);
        }
    }

    /// <summary>
    /// Accumulates hash state across multiple FeedData calls.
    /// All algorithms run on the calling thread — no background threads.
    /// The caller (imaging engine) is responsible for threading strategy.
    /// </summary>
    public sealed class StreamingHasher : IStreamingHasher
    {
        private readonly Dictionary<HashAlgorithmType, IncrementalHash> _hashers;
        private bool _finalized;
        private bool _disposed;

        public StreamingHasher(HashAlgorithmType[] algorithms)
        {
            _hashers = new Dictionary<HashAlgorithmType, IncrementalHash>();

            foreach (var alg in algorithms)
            {
                HashAlgorithmName name = alg switch
                {
                    HashAlgorithmType.MD5 => HashAlgorithmName.MD5,
                    HashAlgorithmType.SHA1 => HashAlgorithmName.SHA1,
                    HashAlgorithmType.SHA256 => HashAlgorithmName.SHA256,
                    _ => throw new NotSupportedException($"Hash algorithm not supported: {alg}")
                };

                _hashers[alg] = IncrementalHash.CreateHash(name);
            }
        }

        /// <inheritdoc/>
        public void FeedData(byte[] buffer, int offset, int count)
        {
            if (_finalized) throw new InvalidOperationException("Hasher already finalized.");
            foreach (var h in _hashers.Values)
                h.AppendData(buffer, offset, count);
        }

        /// <inheritdoc/>
        public void FeedData(ReadOnlySpan<byte> data)
        {
            if (_finalized) throw new InvalidOperationException("Hasher already finalized.");
            foreach (var h in _hashers.Values)
                h.AppendData(data);
        }

        /// <inheritdoc/>
        public Dictionary<HashAlgorithmType, string> Finalize()
        {
            if (_finalized) throw new InvalidOperationException("Hasher already finalized.");
            _finalized = true;

            var results = new Dictionary<HashAlgorithmType, string>();
            foreach (var (alg, h) in _hashers)
            {
                byte[] hash = h.GetHashAndReset();
                results[alg] = Convert.ToHexString(hash).ToLowerInvariant();
            }
            return results;
        }

        /// <inheritdoc/>
        public Dictionary<HashAlgorithmType, string> GetIntermediateValues()
        {
            // Clone each hasher's state by calling GetCurrentHash (non-destructive in .NET 8)
            var results = new Dictionary<HashAlgorithmType, string>();
            foreach (var (alg, h) in _hashers)
            {
                try
                {
                    // GetCurrentHash does not reset the hasher
                    byte[] hash = h.GetCurrentHash();
                    results[alg] = Convert.ToHexString(hash).ToLowerInvariant();
                }
                catch
                {
                    results[alg] = "(computing...)";
                }
            }
            return results;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var h in _hashers.Values) h.Dispose();
        }
    }
}
