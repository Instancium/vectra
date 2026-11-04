#nullable enable
namespace Instancium.Vectra.Core.Testing;

using Instancium.Vectra.Core.Contracts;
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Deterministic in-process embedding provider utilizing SHA-256 hash derivation.
/// Designed for offline testing, benchmarks, and out-of-the-box local scenarios without requiring
/// external network services, GPU accelerators, or heavy ONNX runtimes.
/// </summary>
public sealed class HashedStubEmbeddingProvider : IEmbeddingProvider
{
    public const string DefaultModelId = "hashed-stub-embedding";
    public const int DefaultDimension = 128;

    public string ModelId { get; }
    public int Dimension { get; }

    public HashedStubEmbeddingProvider(int dimension = DefaultDimension, string modelId = DefaultModelId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        Dimension = dimension;
        ModelId = modelId;
    }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] textBytes = Encoding.UTF8.GetBytes(text);
        byte[] hash = SHA256.HashData(textBytes);

        int seed = 0;
        for (int i = 0; i < hash.Length; i += sizeof(int))
        {
            seed ^= BinaryPrimitives.ReadInt32LittleEndian(hash.AsSpan(i, sizeof(int)));
        }

        var rng = new Random(seed);
        var vector = new float[Dimension];
        double sumSquares = 0.0;

        for (int i = 0; i < Dimension; i++)
        {
            float val = (float)(rng.NextDouble() * 2.0 - 1.0);
            vector[i] = val;
            sumSquares += (double)val * val;
        }

        // Unit normalization ensures consistent scoring across Cosine, DotProduct, and Euclidean kernels
        float norm = (float)Math.Sqrt(sumSquares);
        if (norm > 1e-12f)
        {
            for (int i = 0; i < Dimension; i++)
            {
                vector[i] /= norm;
            }
        }

        return ValueTask.FromResult<ReadOnlyMemory<float>>(vector);
    }
}
