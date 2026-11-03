namespace Instancium.Vectra.Search;

using System.Diagnostics;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Brute-force search kernel: scans all vectors in the snapshot and returns the top-K
/// candidates by similarity score.
///
/// Contract (TZ §5.3):
///   - Results are ordered by VectorScore descending, with deterministic tie-break by VectorId ascending.
///   - FinalScore equals VectorScore because no reranker runs at this layer.
///   - Overfetch is guaranteed to be ≥ TopK (for future approximate search kernels like HNSW/IVF).
///
/// This kernel always performs a full scan (no approximate indexing). For quantized collections
/// (P7), a two-pass approach will be used: approximate selection → exact rescoring on float32.
/// </summary>
public sealed class BruteForceSearchKernel : ISearchKernel
{
    private readonly ISimilarityKernel _kernel;

    public BruteForceSearchKernel(ISimilarityKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        _kernel = kernel;
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        IVectraSnapshot snapshot,
        SearchQuery query,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (query.Vector.Length != snapshot.Dimension)
        {
            throw new ArgumentException(
                $"Query dimension {query.Vector.Length} does not match snapshot dimension {snapshot.Dimension}.",
                nameof(query));
        }

        int overfetch = Math.Max(options.Overfetch ?? options.TopK, options.TopK);
        var stopwatch = Stopwatch.StartNew();

        var hits = new List<VectorSearchHit>(checked((int)snapshot.VectorCount));
        ReadOnlySpan<float> querySpan = query.Vector.Span;

        for (long slot = 0; slot < snapshot.VectorCount; slot++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySpan<float> candidate = snapshot.GetVector(slot);
            float score = _kernel.Score(querySpan, candidate);
            long vectorId = snapshot.GetVectorId(slot);

            hits.Add(new VectorSearchHit(
                vectorId,
                score,
                score,
                payload: null,
                diagnostics: null));
        }

        IReadOnlyList<VectorSearchHit> sorted = hits
            .OrderByDescending(hit => hit.VectorScore)
            .ThenBy(hit => hit.VectorId)
            .Take(overfetch)
            .ToList();

        stopwatch.Stop();

        if (options.IncludeDiagnostics)
        {
            var diagnostics = new SearchDiagnostics(
                scannedVectors: checked((int)snapshot.VectorCount),
                recallTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                rerankTimeMs: 0.0,
                kernelUsed: _kernel.GetType().Name);

            sorted = sorted
                .Select(hit => new VectorSearchHit(
                    hit.VectorId,
                    hit.VectorScore,
                    hit.FinalScore,
                    hit.Payload,
                    diagnostics))
                .ToList();
        }

        return Task.FromResult(sorted);
    }
}
