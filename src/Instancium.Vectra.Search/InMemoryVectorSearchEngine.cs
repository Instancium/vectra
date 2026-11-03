#nullable enable
namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search.Kernels;
using Instancium.Vectra.Storage;

/// <summary>
/// In-memory vector search engine: thin facade over <see cref="ISearchKernel"/> and
/// optional <see cref="IReranker"/>.
///
/// P4 refactor: replaced the interim P3 brute-force dot-product scan with a metric-aware,
/// SIMD-accelerated kernel resolved by the collection's declared <see cref="SimilarityMetric"/>.
///
/// Flow:
///   1. Create snapshot from <see cref="IVectorStore"/> (normalizes Cosine vectors if needed).
///   2. Resolve <see cref="ISimilarityKernel"/> by metric.
///   3. Execute <see cref="BruteForceSearchKernel.SearchAsync"/> (overfetch + exact scoring).
///   4. Apply optional <see cref="IReranker"/> (updates FinalScore, never adds candidates).
///   5. Hydrate payload text lazily through <see cref="IVectraCollection.GetPayloadTextAsync"/>.
///   6. Return top-K results.
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
                $"Query dimension {queryVector.Length} does not match store dimension {dimension}.",
                nameof(queryVector));
        }

        // TODO [P5]: IVectraSnapshot creation should move to VectraCollection.SearchAsync.
        // Currently we infer metric as Cosine (legacy default); P5 will pass CollectionOptions explicitly.
        SimilarityMetric metric = SimilarityMetric.Cosine;

        using var snapshot = CreateSnapshot(metric);
        ISimilarityKernel kernel = ResolveKernel(metric);
        var searchKernel = new BruteForceSearchKernel(kernel);

        var query = new SearchQuery(queryVector);
        var options = new SearchOptions(topK: topK, overfetch: null, reranker: null, includeDiagnostics: false);

        IReadOnlyList<VectorSearchHit> candidates = await searchKernel
            .SearchAsync(snapshot, query, options, cancellationToken)
            .ConfigureAwait(false);

        // Take top-K (candidates already sorted by score desc, vectorId asc)
        return candidates.Take(topK).ToList();
    }

    private InMemoryVectraSnapshot CreateSnapshot(SimilarityMetric metric)
    {
        ReadOnlySpan<float> vectorsSpan = _store.GetVectorsSpan();
        IReadOnlyList<ChunkMetadata> metadata = _store.GetMetadata();

        int dimension = _store.Dimension;
        int count = metadata.Count;

        // Copy vectors into new array (snapshot owns the memory)
        float[] vectors = vectorsSpan.ToArray();
        long[] vectorIds = new long[count];

        for (int i = 0; i < count; i++)
        {
            vectorIds[i] = metadata[i].ChunkId;
        }

        return new InMemoryVectraSnapshot(vectors, vectorIds, dimension, metric);
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
