#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record ChunkMetadata
{
    public ChunkMetadata(long chunkId, long fileId, int pageNumber, string snippet)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 0L);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 0L);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet);

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
