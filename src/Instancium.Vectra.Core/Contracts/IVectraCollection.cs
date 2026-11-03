namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// A single named vector collection with fixed data properties
/// (<see cref="CollectionOptions"/>: dimension, metric, model id, quantization).
///
/// Contract:
///   - <see cref="Info"/> reflects the current persisted state and may change between calls
///     as writes commit.
///   - <see cref="AddAsync"/> and <see cref="AddRangeAsync"/> normalize input vectors
///     deterministically (double-accumulation) for <see cref="SimilarityMetric.Cosine"/>
///     collections and accept them as-is for DotProduct/Euclidean.
///   - <see cref="SearchAsync"/> returns hits whose <see cref="VectorSearchHit.VectorScore"/>
///     is the raw kernel output and whose <see cref="VectorSearchHit.FinalScore"/> equals
///     <see cref="VectorSearchHit.VectorScore"/> when <see cref="SearchOptions.Reranker"/>
///     is <c>null</c>, or the reranker's score otherwise.
///   - <see cref="GetPayloadTextAsync"/> is the single lazy-read path for payload text;
///     it is the only legal way for rerankers and consumers to materialize payload.
/// </summary>
public interface IVectraCollection
{
    /// <summary>Collection name, unique within an <see cref="IVectraEngine"/>.</summary>
    string Name { get; }

    /// <summary>Live snapshot of the collection's identity and size.</summary>
    CollectionInfo Info { get; }

    /// <summary>
    /// Adds a single vector. If <see cref="VectorEntry.VectorId"/> is <c>null</c>,
    /// the engine assigns a new positive identifier and returns it.
    /// </summary>
    ValueTask<long> AddAsync(VectorEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Bulk variant of <see cref="AddAsync"/>; preserves input order of results.</summary>
    Task AddRangeAsync(ReadOnlyMemory<VectorEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a vector by id. Returns <c>false</c> if the id was not present.</summary>
    ValueTask<bool> RemoveAsync(long vectorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the collection. Honors <see cref="SearchOptions.Overfetch"/>, optional
    /// <see cref="SearchOptions.Reranker"/> and optional <see cref="SearchOptions.IncludeDiagnostics"/>.
    /// Filter expressions (<see cref="SearchQuery.Filter"/>) are reserved; until an evaluator
    /// exists, kernels must throw <see cref="NotSupportedException"/> when a filter is supplied.
    /// </summary>
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        SearchQuery query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lazy-reads the stored payload text for the given vector id.</summary>
    ValueTask<string?> GetPayloadTextAsync(long vectorId, CancellationToken cancellationToken = default);
}
