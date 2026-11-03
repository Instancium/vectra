namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Top-level entry point of the vector engine. Owns the collection catalog and the
/// base storage directory. Disposable: releasing the engine releases every open
/// collection and their background compaction tasks.
///
/// Contract (TZ §5.5):
///   - <see cref="GetOrOpenCollection"/> is idempotent: opening an existing collection
///     with identical <see cref="CollectionOptions"/> returns the same instance; opening
///     with a mismatched <see cref="CollectionOptions.ModelFingerprint"/> throws
///     <c>VectraIncompatibleModelException</c> (introduced in P5).
///   - <see cref="DeleteCollectionAsync"/> removes the catalog entry and schedules
///     physical removal of segments.
/// </summary>
public interface IVectraEngine : IAsyncDisposable
{
    /// <summary>
    /// Opens an existing collection by name, or creates it with <paramref name="options"/>
    /// if none exists. Re-opening with incompatible options throws.
    /// </summary>
    IVectraCollection GetOrOpenCollection(string name, CollectionOptions options);

    /// <summary>Lists every collection known to the engine.</summary>
    Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes a collection by name. No-op if the collection does not exist.</summary>
    Task DeleteCollectionAsync(string name, CancellationToken cancellationToken = default);
}
