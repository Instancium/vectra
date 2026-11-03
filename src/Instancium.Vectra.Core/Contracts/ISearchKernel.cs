namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Strategy for approximate or exact vector search.
///
/// Contract (TZ §5.3):
///   - The kernel respects <see cref="SearchOptions.Overfetch"/> as a lower bound:
///     <c>effectiveOverfetch = max(options.Overfetch, options.TopK, strategy-default)</c>,
///     guaranteeing candidate completeness.
///   - Quantized implementations MUST perform a second pass that recomputes scores
///     against the original float32 vectors for the selected candidates.
///   - The output is sorted by <see cref="VectorSearchHit.VectorScore"/> descending
///     with deterministic tie-break by <see cref="VectorSearchHit.VectorId"/> ascending.
///   - The kernel populates only <see cref="VectorSearchHit.VectorScore"/> and
///     <see cref="VectorSearchHit.VectorId"/>; <see cref="VectorSearchHit.FinalScore"/>
///     is set equal to <see cref="VectorSearchHit.VectorScore"/> here, and may be
///     overwritten later by an <see cref="IReranker"/>.
/// </summary>
public interface ISearchKernel
{
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        IVectraSnapshot snapshot,
        SearchQuery query,
        SearchOptions options,
        CancellationToken cancellationToken = default);
}
