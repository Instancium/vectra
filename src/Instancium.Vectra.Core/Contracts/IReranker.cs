namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Extension seam for candidate re-scoring (lexical, hybrid BM25, cross-encoders).
/// Contract: a reranker may reorder or drop candidates but must NEVER add new ones.
/// The engine copies the reranker's score into VectorSearchHit.FinalScore.
/// </summary>
public interface IReranker
{
    Task<IReadOnlyList<VectorSearchHit>> RerankAsync(
        SearchQuery query,
        IReadOnlyList<VectorSearchHit> candidates,
        CancellationToken cancellationToken = default);
}
