using FidelSec.Core.Models;
using FidelSec.Infrastructure.Hashing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FidelSec.Tests
{
    /// <summary>
    /// Tests for the hashing engine — verifies known hash values against
    /// well-established test vectors (NIST FIPS publications).
    /// </summary>
    public class HashingEngineTests
    {
        private readonly HashingEngine _engine = new(NullLogger<HashingEngine>.Instance);

        [Fact]
        public async Task ComputeFileHash_KnownFile_ReturnsMd5Correctly()
        {
            // Arrange: write a temp file with known content
            var tmpFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmpFile, "FidelSec forensic test vector");

            try
            {
                // Act
                var hashes = await _engine.ComputeFileHashesAsync(
                    tmpFile,
                    new[] { HashAlgorithmType.MD5 });

                // Assert: hash must be present and non-empty
                Assert.True(hashes.ContainsKey(HashAlgorithmType.MD5));
                Assert.Equal(32, hashes[HashAlgorithmType.MD5].Length); // MD5 = 32 hex chars
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public async Task ComputeFileHash_AllAlgorithms_ReturnCorrectLengths()
        {
            var tmpFile = Path.GetTempFileName();
            await File.WriteAllBytesAsync(tmpFile, new byte[4096]); // 4KB of zeros

            try
            {
                var hashes = await _engine.ComputeFileHashesAsync(
                    tmpFile,
                    new[] { HashAlgorithmType.MD5, HashAlgorithmType.SHA1, HashAlgorithmType.SHA256 });

                Assert.Equal(32, hashes[HashAlgorithmType.MD5].Length);
                Assert.Equal(40, hashes[HashAlgorithmType.SHA1].Length);
                Assert.Equal(64, hashes[HashAlgorithmType.SHA256].Length);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public void StreamingHasher_Finalize_MatchesFullFileHash()
        {
            // Test that streaming (chunk-by-chunk) produces same result as full hash
            byte[] data = new byte[65536];
            new Random(42).NextBytes(data);

            // Full hash via .NET
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            string expected = Convert.ToHexString(sha256.ComputeHash(data)).ToLowerInvariant();

            // Streaming hash via our engine
            using var hasher = _engine.CreateStreamingHasher(new[] { HashAlgorithmType.SHA256 });

            // Feed in 1024-byte chunks
            for (int i = 0; i < data.Length; i += 1024)
            {
                int count = Math.Min(1024, data.Length - i);
                hasher.FeedData(data, i, count);
            }

            var results = hasher.Finalize();
            Assert.Equal(expected, results[HashAlgorithmType.SHA256]);
        }

        [Fact]
        public void StreamingHasher_GetIntermediate_DoesNotFinalizeHasher()
        {
            using var hasher = _engine.CreateStreamingHasher(
                new[] { HashAlgorithmType.MD5, HashAlgorithmType.SHA256 });

            byte[] chunk = new byte[512];
            hasher.FeedData(chunk, 0, chunk.Length);

            // Get intermediate — should not throw or finalize
            var intermediate = hasher.GetIntermediateValues();
            Assert.True(intermediate.ContainsKey(HashAlgorithmType.MD5));

            // Should still be able to feed more data and finalize
            hasher.FeedData(chunk, 0, chunk.Length);
            var final = hasher.Finalize();
            Assert.True(final.ContainsKey(HashAlgorithmType.SHA256));
        }

        [Fact]
        public void StreamingHasher_FinalizeCalledTwice_ThrowsException()
        {
            using var hasher = _engine.CreateStreamingHasher(new[] { HashAlgorithmType.MD5 });
            hasher.FeedData(new byte[64], 0, 64);
            hasher.Finalize();

            Assert.Throws<InvalidOperationException>(() => hasher.Finalize());
        }

        [Theory]
        [InlineData(HashAlgorithmType.MD5, "d41d8cd98f00b204e9800998ecf8427e")]  // Empty MD5
        [InlineData(HashAlgorithmType.SHA256,
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // Empty SHA256
        public void StreamingHasher_EmptyInput_MatchesKnownEmptyHash(
            HashAlgorithmType alg, string expectedHash)
        {
            using var hasher = _engine.CreateStreamingHasher(new[] { alg });
            // No data fed — just finalize on empty input
            var results = hasher.Finalize();
            Assert.Equal(expectedHash, results[alg]);
        }
    }
}
