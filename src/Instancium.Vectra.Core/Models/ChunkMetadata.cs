#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// In-memory metadata of a stored vector. The text payload is optional: when null,
/// consumers must hydrate it lazily through IChunkTextReader /
/// IVectraCollection.GetPayloadTextAsync instead of expecting inline text.
/// </summary>
public sealed record ChunkMetadata
{
    public ChunkMetadata(long chunkId, long fileId, int pageNumber, string? payload)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);

        ChunkId = chunkId;
        FileId = fileId;
        PageNumber = pageNumber;
        Payload = payload;
    }

    public long ChunkId { get; }

    public long FileId { get; }

    public int PageNumber { get; }

    public string? Payload { get; }
}
