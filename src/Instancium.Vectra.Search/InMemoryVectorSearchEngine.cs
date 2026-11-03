#nullable enable
namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Interim P3 search engine: brute-force dot-product scan over the store's immutable
/// snapshot (no locks, no per-hit allocation beyond the result list).
///
/// Contract (TZ §5.3): results are ordered by VectorScore descending with a deterministic
/// tie-break by VectorId ascending; FinalScore equals VectorScore because no reranker
/// runs at this layer.
///
/// P4 replaces the scoring body with ISearchKernel + IVectraSnapshot (metric-aware,
/// SIMD-accelerated, conformance-tested against ReferenceSimilarityKernel).
/// </summary>
public sealed class InMemoryVectorSearchEngine : IVectorSearchEngine
{
    private readonly IVectorStore _store;

    public InMemoryVectorSearchEngine(IVectorStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        int dimension = _store.Dimension;
        if (dimension == 0)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());
        }

        if (queryVector.Length != dimension)
        {
            throw new ArgumentException(
                "Query dimension " + queryVector.Length + " does not match store dimension " + dimension + ".",
                nameof(queryVector));
        }

        // Two independent immutable snapshots (vectors, metadata) may differ by a concurrent
        // write; the minimum count keeps slot pairing consistent.
        ReadOnlySpan<float> vectors = _store.GetVectorsSpan();
        IReadOnlyList<ChunkMetadata> metadata = _store.GetMetadata();
        int count = Math.Min(metadata.Count, vectors.Length / dimension);

        var hits = new List<VectorSearchHit>(Math.Min(count, topK));
        ReadOnlySpan<float> query = queryVector.Span;

        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySpan<float> candidate = vectors.Slice(i * dimension, dimension);
            float dot = 0f;
            for (int d = 0; d < dimension; d++)
            {
                dot += query[d] * candidate[d];
            }

            hits.Add(new VectorSearchHit(
                VectorId: metadata[i].ChunkId,
                VectorScore: dot,
                FinalScore: dot,
                Payload: metadata[i].Payload,
                Diagnostics: null));
        }

        IReadOnlyList<VectorSearchHit> result = hits
            .OrderByDescending(hit => hit.VectorScore)
            .ThenBy(hit => hit.VectorId)
            .Take(topK)
            .ToList();

        return Task.FromResult(result);
    }
}
