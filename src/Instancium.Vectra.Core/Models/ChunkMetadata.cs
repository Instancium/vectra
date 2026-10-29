#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record ChunkMetadata
{
    public ChunkMetadata(long chunkId, long fileId, int pageNumber, string snippet)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentNullException.ThrowIfNull(snippet);

        ChunkId = chunkId;
        FileId = fileId;
        PageNumber = pageNumber;
        Snippet = snippet;
    }

    public long ChunkId { get; }

    public long FileId { get; }

    public int PageNumber { get; }

    public string Snippet { get; }
}


















































































































































