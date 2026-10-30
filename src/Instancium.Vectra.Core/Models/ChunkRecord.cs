#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// A single chunk destined for the on-disk index: identity, full text payload and embedding vector.
/// Unlike <see cref="VectorIndexEntry"/>, which serves search results, this model is the write-side input.
/// </summary>
public sealed record ChunkRecord
{
    public ChunkRecord(long chunkId, long fileId, int pageNumber, string text, ReadOnlyMemory<float> vector, bool isActive = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        }

        ChunkId = chunkId;
        FileId = fileId;
        PageNumber = pageNumber;
        Text = text;
        Vector = vector;
        IsActive = isActive;
    }

    public long ChunkId { get; }
    public long FileId { get; }
    public int PageNumber { get; }
    public string Text { get; }
    public ReadOnlyMemory<float> Vector { get; }
    public bool IsActive { get; }
}
