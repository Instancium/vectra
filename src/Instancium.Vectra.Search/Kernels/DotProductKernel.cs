namespace Instancium.Vectra.Search.Kernels;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// SIMD-accelerated dot-product kernel using AVX2 when available, otherwise a scalar fallback.
/// </summary>
public sealed class DotProductKernel : ISimilarityKernel
{
    public SimilarityMetric Metric => SimilarityMetric.DotProduct;

    public float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
    {
        if (query.Length != candidate.Length)
        {
            throw new ArgumentException(
                $"Vector dimension mismatch: query={query.Length}, candidate={candidate.Length}.",
                nameof(query));
        }

        if (Avx2.IsSupported && query.Length >= Vector256<float>.Count)
        {
            return ScoreAvx2(query, candidate);
        }

        return ScoreScalar(query, candidate);
    }

    private static float ScoreAvx2(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int vectorSize = Vector256<float>.Count;
        int limit = a.Length - (a.Length % vectorSize);

        Vector256<float> sum = Vector256<float>.Zero;

        for (int i = 0; i < limit; i += vectorSize)
        {
            Vector256<float> va = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(a[i..]));
            Vector256<float> vb = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(b[i..]));
            sum = Avx2.Add(sum, Avx2.Multiply(va, vb));
        }

        float result = 0f;
        for (int i = 0; i < vectorSize; i++)
        {
            result += sum[i];
        }

        for (int i = limit; i < a.Length; i++)
        {
            result += a[i] * b[i];
        }

        return result;
    }

    private static float ScoreScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }
}
