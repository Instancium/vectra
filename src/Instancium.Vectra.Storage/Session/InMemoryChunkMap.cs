namespace Instancium.Vectra.Storage.Session;

using System.Diagnostics.CodeAnalysis;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// In-memory <see cref="IChunkMap"/> with a primary chunk-id index and a secondary file-id index.
/// </summary>
public sealed class InMemoryChunkMap : IChunkMap
{
    private readonly IReadOnlyList<ChunkMapEntry> _entries;
    private readonly Dictionary<long, ChunkMapEntry> _byChunkId;
    private readonly Dictionary<long, IReadOnlyList<long>> _chunkIdsByFile;

    public InMemoryChunkMap(IEnumerable<ChunkMapEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.OrderBy(entry => entry.ChunkId).ToArray();
        _byChunkId = new Dictionary<long, ChunkMapEntry>(_entries.Count);
        var byFile = new Dictionary<long, List<long>>();

        foreach (ChunkMapEntry entry in _entries)
        {
            if (!_byChunkId.TryAdd(entry.ChunkId, entry))
            {
                throw new ArgumentException($"Duplicate chunk id {entry.ChunkId}.", nameof(entries));
            }

            if (!byFile.TryGetValue(entry.FileId, out List<long>? chunkIds))
            {
                chunkIds = new List<long>();
                byFile[entry.FileId] = chunkIds;
            }

            chunkIds.Add(entry.ChunkId);
        }

        _chunkIdsByFile = byFile.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<long>)pair.Value.ToArray());
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public IReadOnlyList<ChunkMapEntry> Entries => _entries;

    /// <inheritdoc />
    public ChunkMapEntry Get(long chunkId)
    {
        if (_byChunkId.TryGetValue(chunkId, out ChunkMapEntry? entry))
        {
            return entry;
        }

        throw new KeyNotFoundException($"Chunk {chunkId} is not present in the chunk map.");
    }

    /// <inheritdoc />
    public bool TryGet(long chunkId, [NotNullWhen(true)] out ChunkMapEntry? entry) =>
        _byChunkId.TryGetValue(chunkId, out entry);

    /// <inheritdoc />
    public IReadOnlyList<long> GetChunkIdsByFile(long fileId) =>
        _chunkIdsByFile.TryGetValue(fileId, out IReadOnlyList<long>? chunkIds) ? chunkIds : Array.Empty<long>();
}
