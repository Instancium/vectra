namespace Instancium.Vectra.Search.Kernels;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Exact exhaustive search across a snapshot using the metric-specific kernel.
/// </summary>
public sealed class BruteForceSearchKernel : ISearchKernel
{
    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        IVectraSnapshot snapshot,
        SearchQuery query,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (snapshot.VectorCount == 0)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());
        }

        if (query.Vector.Length != snapshot.Dimension)
        {
            throw new ArgumentException(
                $"Query dimension {query.Vector.Length} does not match snapshot dimension {snapshot.Dimension}.",
                nameof(query));
        }

        ISimilarityKernel kernel = ResolveKernel(snapshot.Metric);
        var hits = new List<VectorSearchHit>((int)Math.Min(snapshot.VectorCount, int.MaxValue));

        for (long i = 0; i < snapshot.VectorCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySpan<float> candidate = snapshot.GetVector(i);
            float score = kernel.Score(query.Vector.Span, candidate);
            hits.Add(new VectorSearchHit(snapshot.GetVectorId(i), score, score, null));
        }

        IReadOnlyList<VectorSearchHit> ordered = hits
            .OrderByDescending(hit => hit.VectorScore)
            .ThenBy(hit => hit.VectorId)
            .ToList();

        return Task.FromResult<IReadOnlyList<VectorSearchHit>>(ordered);
    }

    private static ISimilarityKernel ResolveKernel(SimilarityMetric metric) => metric switch
    {
        SimilarityMetric.Cosine => new CosineKernel(),
        SimilarityMetric.DotProduct => new DotProductKernel(),
        SimilarityMetric.Euclidean => new EuclideanKernel(),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported metric.")
    };
}
