namespace Instancium.Vectra.Core.Contracts;

using System.Diagnostics.CodeAnalysis;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Lookup structure over the loaded chunk map: chunk identity, source file, page,
/// byte range of the chunk text inside <c>chunks.bin</c> and the soft-deletion flag.
/// </summary>
public interface IChunkMap
{
    /// <summary>Total number of entries, including inactive ones.</summary>
    int Count { get; }

    /// <summary>All entries ordered by chunk id.</summary>
    IReadOnlyList<ChunkMapEntry> Entries { get; }

    /// <summary>Returns the entry for <paramref name="chunkId"/>.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the chunk id is unknown.</exception>
    ChunkMapEntry Get(long chunkId);

    /// <summary>Tries to return the entry for <paramref name="chunkId"/>.</summary>
    bool TryGet(long chunkId, [NotNullWhen(true)] out ChunkMapEntry? entry);

    /// <summary>Chunk ids belonging to the given file, ordered by chunk id. Empty when the file is unknown.</summary>
    IReadOnlyList<long> GetChunkIdsByFile(long fileId);
}
