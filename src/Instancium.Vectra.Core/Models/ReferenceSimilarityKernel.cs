namespace Instancium.Vectra.Core.Models;

using Instancium.Vectra.Core.Contracts;

/// <summary>
/// Reference implementation of <see cref="ISimilarityKernel"/> using double-precision
/// accumulation for maximum numerical stability.
/// </summary>
public sealed class ReferenceSimilarityKernel : ISimilarityKernel
{
    public SimilarityMetric Metric { get; }

    public ReferenceSimilarityKernel(SimilarityMetric metric)
    {
        Metric = metric;
    }

    public float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
    {
        if (query.Length != candidate.Length)
        {
            throw new ArgumentException(
                $"Vector dimension mismatch: query={query.Length}, candidate={candidate.Length}.",
                nameof(query));
        }

        return Metric switch
        {
            SimilarityMetric.Cosine => CosineScore(query, candidate),
            SimilarityMetric.DotProduct => DotProductScore(query, candidate),
            SimilarityMetric.Euclidean => EuclideanScore(query, candidate),
            _ => throw new ArgumentOutOfRangeException(nameof(Metric), Metric, "Unknown similarity metric.")
        };
    }

    private static float CosineScore(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            double ai = a[i];
            double bi = b[i];
            dot += ai * bi;
            normA += ai * ai;
            normB += bi * bi;
        }

        double denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        if (denominator < 1e-12)
        {
            return 0.0f;
        }

        return (float)(dot / denominator);
    }

    private static float DotProductScore(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
        }

        return (float)dot;
    }

    private static float EuclideanScore(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double sumSquaredDiff = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            double diff = a[i] - b[i];
            sumSquaredDiff += diff * diff;
        }

        double distance = Math.Sqrt(sumSquaredDiff);
        return (float)(1.0 / (1.0 + distance));
    }
}
