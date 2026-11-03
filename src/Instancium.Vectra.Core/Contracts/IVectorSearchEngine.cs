namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// In-memory vector search over the current store contents.
/// Interim shape (P4): a thin wrapper over <see cref="ISearchKernel"/> with optional
/// <see cref="SearchOptions.Reranker"/> support. In P5 this contract is superseded by
/// <see cref="IVectraCollection.SearchAsync"/> and will be removed.
/// </summary>
public interface IVectorSearchEngine
{
    /// <summary>
    /// Searches the store using the provided query and options.
    /// When <see cref="SearchOptions.Reranker"/> is not null, the engine applies it after
    /// recall and guarantees that <see cref="VectorSearchHit.FinalScore"/> reflects the
    /// reranker's score.
    /// </summary>
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        SearchQuery query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Backward-compatible overload for interim consumers that do not supply options.
    /// </summary>
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(new SearchQuery(queryVector), new SearchOptions(topK: topK), cancellationToken);
    }
}