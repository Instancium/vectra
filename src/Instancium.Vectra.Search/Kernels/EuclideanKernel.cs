namespace Instancium.Vectra.Search.Kernels;

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// SIMD-accelerated Euclidean distance kernel. Higher score means more similar.
/// </summary>
public sealed class EuclideanKernel : ISimilarityKernel
{
    public SimilarityMetric Metric => SimilarityMetric.Euclidean;

    public float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
    {
        if (query.Length != candidate.Length)
        {
            throw new ArgumentException(
                $"Vector dimension mismatch: query={query.Length}, candidate={candidate.Length}.",
                nameof(query));
        }

        float distance = ComputeDistance(query, candidate);
        return 1.0f / (1.0f + distance);
    }

    private static float ComputeDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (Avx2.IsSupported && a.Length >= Vector256<float>.Count)
        {
            return ComputeDistanceAvx2(a, b);
        }

        return ComputeDistanceScalar(a, b);
    }

    private static float ComputeDistanceAvx2(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int vectorSize = Vector256<float>.Count;
        int limit = a.Length - (a.Length % vectorSize);

        Vector256<float> sumSquaredDiff = Vector256<float>.Zero;

        for (int i = 0; i < limit; i += vectorSize)
        {
            Vector256<float> va = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(a[i..]));
            Vector256<float> vb = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(b[i..]));
            Vector256<float> diff = Avx2.Subtract(va, vb);
            sumSquaredDiff = Avx2.Add(sumSquaredDiff, Avx2.Multiply(diff, diff));
        }

        float result = 0f;
        for (int i = 0; i < vectorSize; i++)
        {
            result += sumSquaredDiff[i];
        }

        for (int i = limit; i < a.Length; i++)
        {
            float diff = a[i] - b[i];
            result += diff * diff;
        }

        return MathF.Sqrt(result);
    }

    private static float ComputeDistanceScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float sumSquaredDiff = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            float diff = a[i] - b[i];
            sumSquaredDiff += diff * diff;
        }

        return MathF.Sqrt(sumSquaredDiff);
    }
}
