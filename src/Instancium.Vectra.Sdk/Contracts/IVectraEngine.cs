namespace Instancium.Vectra.Sdk.Contracts;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Sdk.Models;

/// <summary>
/// Unified high-level facade for document indexing and vector search operations.
/// </summary>
public interface IVectraEngine
{
    /// <summary>
    /// Scans, chunks, embeds, and incrementally persists documents from the specified folder
    /// into the profile-isolated storage.
    /// </summary>
    /// <param name="profileId">The profile or tenant identifier.</param>
    /// <param name="folderPath">The root directory containing documents to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexing execution metrics and manifest summary.</returns>
    Task<IndexResult> IndexFolderAsync(
        string profileId,
        string folderPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs semantic vector search across chunks in the profile index,
    /// returning hydrated chunks with their original text snippets from disk storage.
    /// </summary>
    /// <param name="profileId">The profile or tenant identifier.</param>
    /// <param name="query">Natural language search query.</param>
    /// <param name="topK">Maximum number of relevant chunks to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching chunks with similarity scores and populated text snippets.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string profileId,
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves current index metrics, schema version, and readiness for the given profile.
    /// </summary>
    /// <param name="profileId">The profile or tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata status of the index.</returns>
    Task<IndexStatus> GetStatusAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}
