#nullable enable
namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search.Kernels;
using System.Diagnostics;

/// <summary>
/// In-memory vector search engine: thin facade over <see cref="ISearchKernel"/> with optional
/// <see cref="IReranker"/> support (P4 refactor).
///
/// The engine depends ONLY on Core contracts: the snapshot is obtained from
/// <see cref="IVectorStore.CreateSnapshot"/> (implemented in Storage and injected via DI),
/// which keeps the architecture rule
/// "Search references Core + System + Microsoft.Extensions.* only" intact.
///
/// Flow: snapshot ← store → kernel resolve by metric → brute-force scan (overfetch ≥ topK,
/// deterministic tie-break) → optional rerank → payload hydration from store metadata → top-K.
///
/// TODO [P5]: metric must come from CollectionOptions owned by VectraCollection, and payload
/// must hydrate lazily through IVectraCollection.GetPayloadTextAsync; until then the interim
/// engine assumes Cosine (legacy default) and hydrates from IVectorStore.GetMetadata().
/// Query normalization for Cosine is performed here to satisfy the CosineKernel contract;
/// in P5 this responsibility moves to VectraCollection.SearchAsync.
/// </summary>
public sealed class InMemoryVectorSearchEngine : IVectorSearchEngine
{
    private readonly IVectorStore _store;

    public InMemoryVectorSearchEngine(IVectorStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        SearchQuery query,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        int dimension = _store.Dimension;
        if (dimension == 0)
        {
            return Array.Empty<VectorSearchHit>();
        }

        if (query.Vector.Length != dimension)
        {
            throw new ArgumentException(
                "Query dimension " + query.Vector.Length + " does not match store dimension " + dimension + ".",
                nameof(query));
        }

        const SimilarityMetric metric = SimilarityMetric.Cosine;

        // CosineKernel operates on L2-normalized vectors. Until VectraCollection (P5) takes
        // ownership of normalization, the interim engine normalizes the query here.
        ReadOnlyMemory<float> queryVector = Normalize(query.Vector);

        IVectraSnapshot snapshot = _store.CreateSnapshot(metric);
        ISearchKernel searchKernel = new BruteForceSearchKernel(SimilarityKernelFactory.Create(metric));
        var normalizedQuery = new SearchQuery(queryVector, query.QueryText, query.Filter);

        IReadOnlyList<VectorSearchHit> candidates = await searchKernel
            .SearchAsync(snapshot, normalizedQuery, options, cancellationToken)
            .ConfigureAwait(false);

        SearchDiagnostics? recallDiagnostics = null;
        if (options.IncludeDiagnostics && candidates.Count > 0)
        {
            recallDiagnostics = candidates[0].Diagnostics;
        }

        IReadOnlyList<VectorSearchHit> finalHits = candidates;
        double rerankTimeMs = 0.0;

        if (options.Reranker is not null)
        {
            var rerankStopwatch = Stopwatch.StartNew();
            IReadOnlyList<VectorSearchHit> reranked = await options.Reranker
                .RerankAsync(query, candidates, cancellationToken)
                .ConfigureAwait(false);
            rerankTimeMs = rerankStopwatch.Elapsed.TotalMilliseconds;

            ValidateRerankerOutput(candidates, reranked);
            finalHits = reranked;
        }

        IReadOnlyList<VectorSearchHit> hydrated = HydratePayloads(finalHits, options.TopK);

        if (options.IncludeDiagnostics)
        {
            hydrated = AttachDiagnostics(hydrated, recallDiagnostics, rerankTimeMs, options.Reranker is not null);
        }

        return hydrated;
    }

    private static void ValidateRerankerOutput(
        IReadOnlyList<VectorSearchHit> candidates,
        IReadOnlyList<VectorSearchHit> reranked)
    {
        var candidateIds = new HashSet<long>(candidates.Select(hit => hit.VectorId));

        foreach (VectorSearchHit hit in reranked)
        {
            if (!candidateIds.Contains(hit.VectorId))
            {
                throw new InvalidOperationException(
                    "Reranker returned a vector id " + hit.VectorId + " that was not present in the candidate set.");
            }
        }
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

    private static IReadOnlyList<VectorSearchHit> AttachDiagnostics(
        IReadOnlyList<VectorSearchHit> hits,
        SearchDiagnostics? recallDiagnostics,
        double rerankTimeMs,
        bool rerankerUsed)
    {
        if (!rerankerUsed)
        {
            // BruteForceSearchKernel already populated diagnostics correctly.
            return hits;
        }

        int scannedVectors = recallDiagnostics?.ScannedVectors ?? 0;
        double recallTimeMs = recallDiagnostics?.RecallTimeMs ?? 0.0;
        string? kernelUsed = recallDiagnostics?.KernelUsed;

        var diagnostics = new SearchDiagnostics(scannedVectors, recallTimeMs, rerankTimeMs, kernelUsed);
        var result = new List<VectorSearchHit>(hits.Count);

        foreach (VectorSearchHit hit in hits)
        {
            result.Add(new VectorSearchHit(
                hit.VectorId,
                hit.VectorScore,
                hit.FinalScore,
                hit.Payload,
                diagnostics));
        }

        return result;
    }

    private static ReadOnlyMemory<float> Normalize(ReadOnlyMemory<float> vector)
    {
        double sumOfSquares = 0.0;
        foreach (float value in vector.Span)
        {
            sumOfSquares += (double)value * value;
        }

        float norm = (float)Math.Sqrt(sumOfSquares);
        if (norm < 1e-12f)
        {
            return vector;
        }

        var normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector.Span[i] / norm;
        }

        return normalized;
    }
}