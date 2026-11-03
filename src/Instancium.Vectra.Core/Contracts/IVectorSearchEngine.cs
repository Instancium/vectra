namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// In-memory vector search over the current store contents.
/// Interim shape (P4): a thin wrapper over <see cref="ISearchKernel"/> with optional
/// <see cref="SearchOptions.Reranker"/> support. In P5 this contract is superseded by
/// <see cref="IVectraCollection.SearchAsync"/> and will be removed.
///
/// A convenience overload for callers that do not supply <see cref="SearchOptions"/> is
/// provided as an extension method in <see cref="VectorSearchEngineExtensions"/> to keep
/// this interface minimal (one public overload, no RS0026/RS0027 violations).
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
}