#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record TextChunkingOptions
{
    public const int DefaultChunkSizeChars = 1200;
    public const int DefaultChunkOverlapChars = 200;

    public TextChunkingOptions()
        : this(DefaultChunkSizeChars, DefaultChunkOverlapChars)
    {
    }

    public TextChunkingOptions(int chunkSizeChars, int chunkOverlapChars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSizeChars, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlapChars);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkOverlapChars, chunkSizeChars);

        ChunkSizeChars = chunkSizeChars;
        ChunkOverlapChars = chunkOverlapChars;
    }

    public int ChunkSizeChars { get; }

    public int ChunkOverlapChars { get; }
}
