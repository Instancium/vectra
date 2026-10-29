#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// A byte range of a chunk text inside <c>chunks.bin</c>.
/// </summary>
/// <param name="Offset">Zero-based byte offset of the UTF-8 chunk text.</param>
/// <param name="Length">Byte length of the UTF-8 chunk text.</param>
public sealed record ChunkTextRange
{
    public ChunkTextRange(long offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Offset = offset;
        Length = length;
    }

    public long Offset { get; }

    public int Length { get; }
}
