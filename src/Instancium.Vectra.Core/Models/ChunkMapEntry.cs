#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// One entry of the loaded chunk map (<c>chunk.map.json</c>): chunk identity, source reference
/// and the byte range of the chunk text inside <c>chunks.bin</c>.
/// </summary>
public sealed record ChunkMapEntry
{
    public ChunkMapEntry(long chunkId, long fileId, int pageNumber, long offset, int length, bool isActive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        ChunkId = chunkId;
        FileId = fileId;
        PageNumber = pageNumber;
        Offset = offset;
        Length = length;
        IsActive = isActive;
    }

    public long ChunkId { get; }
    public long FileId { get; }
    public int PageNumber { get; }
    public long Offset { get; }
    public int Length { get; }
    public bool IsActive { get; }
}
