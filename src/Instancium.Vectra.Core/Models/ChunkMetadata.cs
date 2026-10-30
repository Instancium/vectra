#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Metadata of an indexed chunk bound to a vector store entry.
/// </summary>
/// <param name="ChunkId">Numeric chunk identifier.</param>
/// <param name="FileId">Numeric identifier of the source file.</param>
/// <param name="PageNumber">1-based page number the chunk originated from.</param>
/// <param name="Text">
/// Full chunk text. May be empty when the text is resolved lazily by the retrieval layer
/// (disk index sessions, TZ-2 stages 2-3); must never be null.
/// </param>
public sealed record ChunkMetadata
{
    public ChunkMetadata(long chunkId, long fileId, int pageNumber, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentNullException.ThrowIfNull(text);

        ChunkId = chunkId;
        FileId = fileId;
        PageNumber = pageNumber;
        Text = text;
    }

    public long ChunkId { get; }

    public long FileId { get; }

    public int PageNumber { get; }

    public string Text { get; }

    /// <summary>
    /// Backward-compatible alias for <see cref="Text"/> used by the search and presentation layers.
    /// </summary>
    public string Snippet => Text;
}


















































































































































