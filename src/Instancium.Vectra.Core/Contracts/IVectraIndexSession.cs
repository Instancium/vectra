namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// A loaded index session (TZ-2 stage 2): vectors live in <see cref="IVectorStore"/>,
/// the chunk map in <see cref="IChunkMap"/>, and chunk texts are read lazily from disk.
/// All members except <see cref="OpenAsync"/> and <see cref="VectorStore"/> throw
/// <see cref="InvalidOperationException"/> until the session is open.
/// </summary>
public interface IVectraIndexSession
{
    /// <summary>Opens the index stored in <paramref name="indexDirectory"/> and populates the session.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the index is incompatible with the configured embedding model (risk R-3)
    /// or when its artifacts are inconsistent with the manifest.
    /// </exception>
    Task OpenAsync(string indexDirectory, CancellationToken cancellationToken = default);

    /// <summary>Vector store populated with all index vectors; searchable after open.</summary>
    IVectorStore VectorStore { get; }

    /// <summary>Chunk map loaded from <c>chunk.map.json</c>.</summary>
    IChunkMap ChunkMap { get; }

    /// <summary>Manifest of the opened index.</summary>
    DiskIndexManifest Manifest { get; }

    /// <summary>File states loaded from <c>file.state.json</c>; input for incremental indexing and doctor.</summary>
    IReadOnlyList<FileState> FileStates { get; }

    /// <summary>Lazily reads the full text of the chunk from <c>chunks.bin</c>.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the chunk id is unknown.</exception>
    ValueTask<string> GetChunkTextAsync(long chunkId, CancellationToken cancellationToken = default);
}
