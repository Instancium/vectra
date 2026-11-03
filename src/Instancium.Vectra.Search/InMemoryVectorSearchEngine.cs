#nullable enable
namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search.Kernels;

/// <summary>
/// In-memory vector search engine: thin facade over <see cref="ISearchKernel"/>.
///
/// P4 refactor: the interim P3 inline dot-product scan is replaced by a metric-aware,
/// SIMD-accelerated kernel. The engine depends ONLY on Core contracts: the snapshot is
/// obtained from <see cref="IVectorStore.CreateSnapshot"/> (implemented in Storage and
/// injected via DI), which keeps the architecture rule
/// "Search references Core + System + Microsoft.Extensions.* only" intact.
///
/// Flow: snapshot ← store → kernel resolve by metric → brute-force scan (overfetch ≥ topK,
/// deterministic tie-break) → payload hydration from store metadata → top-K.
///
/// TODO [P5]: metric must come from CollectionOptions owned by VectraCollection, and payload
/// must hydrate lazily through IVectraCollection.GetPayloadTextAsync; until then the interim
/// engine assumes Cosine (legacy default) and hydrates from IVectorStore.GetMetadata().
/// </summary>
public sealed class InMemoryVectorSearchEngine : IVectorSearchEngine
{
    private readonly IVectorStore _store;

    public InMemoryVectorSearchEngine(IVectorStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        int dimension = _store.Dimension;
        if (dimension == 0)
        {
            return Array.Empty<VectorSearchHit>();
        }

        if (queryVector.Length != dimension)
        {
            throw new ArgumentException(
                "Query dimension " + queryVector.Length + " does not match store dimension " + dimension + ".",
                nameof(queryVector));
        }

        const SimilarityMetric metric = SimilarityMetric.Cosine;

        IVectraSnapshot snapshot = _store.CreateSnapshot(metric);
        ISearchKernel searchKernel = new BruteForceSearchKernel(ResolveKernel(metric));

        var query = new SearchQuery(queryVector);
        var options = new SearchOptions(topK: topK);

        IReadOnlyList<VectorSearchHit> candidates = await searchKernel
            .SearchAsync(snapshot, query, options, cancellationToken)
            .ConfigureAwait(false);

        return HydratePayloads(candidates, topK);
    }

    private IReadOnlyList<VectorSearchHit> HydratePayloads(
        IReadOnlyList<VectorSearchHit> candidates,
        int topK)
    {
        int take = Math.Min(candidates.Count, topK);
        if (take == 0)
        {
            return Array.Empty<VectorSearchHit>();
        }

        IReadOnlyList<ChunkMetadata> metadata = _store.GetMetadata();
        var payloadById = new Dictionary<long, string?>(metadata.Count);
        foreach (ChunkMetadata entry in metadata)
        {
            payloadById[entry.ChunkId] = entry.Payload;
        }

        var result = new List<VectorSearchHit>(take);
        for (int i = 0; i < take; i++)
        {
            VectorSearchHit hit = candidates[i];
            payloadById.TryGetValue(hit.VectorId, out string? payload);
            result.Add(new VectorSearchHit(
                hit.VectorId,
                hit.VectorScore,
                hit.FinalScore,
                payload,
                hit.Diagnostics));
        }

        return result;
    }

    private static ISimilarityKernel ResolveKernel(SimilarityMetric metric)
    {
        return metric switch
        {
            SimilarityMetric.Cosine => new CosineKernel(),
            SimilarityMetric.DotProduct => new DotProductKernel(),
            SimilarityMetric.Euclidean => new EuclideanKernel(),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown similarity metric.")
        };
    }
}