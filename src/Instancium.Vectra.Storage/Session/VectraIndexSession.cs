namespace Instancium.Vectra.Storage.Session;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Default <see cref="IVectraIndexSession"/> implementation (TZ-2 stage 2).
/// Opens an index written by <see cref="IIndexWriter"/> and materializes it for search:
/// all vectors are loaded into the injected <see cref="IVectorStore"/>, the chunk map is kept
/// in memory, and chunk texts stay on disk until requested through <see cref="GetChunkTextAsync"/>.
/// </summary>
/// <remarks>
/// On open the session validates embedding compatibility (dimension and, when configured, model id —
/// risk R-3) and cross-artifact consistency, failing fast with a pointer to doctor.
/// Inactive chunks are loaded into the vector store as well so that vector positions stay aligned
/// with the chunk map; retrieval must filter them via <see cref="ChunkMapEntry.IsActive"/>.
/// </remarks>
public sealed class VectraIndexSession : IVectraIndexSession
{
    private readonly IIndexReader _reader;
    private readonly IVectorStore _vectorStore;
    private readonly int? _expectedDimension;
    private readonly string? _expectedModelId;

    private string? _indexDirectory;
    private DiskIndexManifest? _manifest;
    private IChunkMap? _chunkMap;
    private IReadOnlyList<FileState>? _fileStates;

    public VectraIndexSession(
        IIndexReader reader,
        IVectorStore vectorStore,
        int? expectedDimension = null,
        string? expectedModelId = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(vectorStore);
        _reader = reader;
        _vectorStore = vectorStore;
        _expectedDimension = expectedDimension;
        _expectedModelId = expectedModelId;
    }

    /// <inheritdoc />
    public IVectorStore VectorStore => _vectorStore;

    /// <inheritdoc />
    public IChunkMap ChunkMap => _chunkMap ?? throw SessionNotOpen();

    /// <inheritdoc />
    public DiskIndexManifest Manifest => _manifest ?? throw SessionNotOpen();

    /// <inheritdoc />
    public IReadOnlyList<FileState> FileStates => _fileStates ?? throw SessionNotOpen();

    /// <inheritdoc />
    public async Task OpenAsync(string indexDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        string fullDirectory = Path.GetFullPath(indexDirectory);

        DiskIndexManifest manifest = await _reader.ReadManifestAsync(fullDirectory, cancellationToken).ConfigureAwait(false);
        ValidateCompatibility(manifest);

        IReadOnlyList<FileState> fileStates = await _reader.ReadFileStatesAsync(fullDirectory, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ChunkMapEntry> chunkEntries = await _reader.ReadChunkMapAsync(fullDirectory, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<float> vectors = await _reader.ReadVectorsAsync(fullDirectory, cancellationToken).ConfigureAwait(false);

        ValidateConsistency(manifest, fileStates, chunkEntries, vectors.Length);

        LoadVectors(manifest.Dimension, chunkEntries, vectors);

        _indexDirectory = fullDirectory;
        _manifest = manifest;
        _fileStates = fileStates;
        _chunkMap = new InMemoryChunkMap(chunkEntries);
    }

    /// <inheritdoc />
    public async ValueTask<string> GetChunkTextAsync(long chunkId, CancellationToken cancellationToken = default)
    {
        string indexDirectory = _indexDirectory ?? throw SessionNotOpen();
        ChunkMapEntry entry = ChunkMap.Get(chunkId);
        return await _reader.ReadChunkTextAsync(indexDirectory, entry.Offset, entry.Length, cancellationToken)
            .ConfigureAwait(false);
    }

    private void ValidateCompatibility(DiskIndexManifest manifest)
    {
        if (_expectedDimension is int expectedDimension && manifest.Dimension != expectedDimension)
        {
            throw new InvalidOperationException(
                $"Index dimension {manifest.Dimension} (model '{manifest.ModelId}') does not match the configured " +
                $"embedding dimension {expectedDimension}. Rebuild the index after changing the embedding model (risk R-3).");
        }

        if (_expectedModelId is { Length: > 0 } expectedModelId &&
            !string.Equals(manifest.ModelId, expectedModelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Index was built with model '{manifest.ModelId}' but the configured model is '{expectedModelId}'. " +
                "Rebuild the index after changing the embedding model (risk R-3).");
        }
    }

    private static void ValidateConsistency(
        DiskIndexManifest manifest,
        IReadOnlyList<FileState> fileStates,
        IReadOnlyList<ChunkMapEntry> chunkEntries,
        int vectorCount)
    {
        if (fileStates.Count != manifest.FileCount)
        {
            throw Corrupted($"file.state.json holds {fileStates.Count} entries but the manifest declares {manifest.FileCount}.");
        }

        if (chunkEntries.Count != manifest.ChunkCount)
        {
            throw Corrupted($"chunk.map.json holds {chunkEntries.Count} entries but the manifest declares {manifest.ChunkCount}.");
        }

        if (vectorCount != manifest.ChunkCount * manifest.Dimension)
        {
            throw Corrupted($"vectors.bin holds {vectorCount} values but the manifest declares {manifest.ChunkCount * manifest.Dimension} values.");
        }

        var fileIds = new HashSet<long>();
        foreach (FileState file in fileStates)
        {
            if (!fileIds.Add(file.FileId))
            {
                throw Corrupted($"file.state.json contains duplicate file id {file.FileId}.");
            }
        }

        foreach (ChunkMapEntry entry in chunkEntries)
        {
            if (!fileIds.Contains(entry.FileId))
            {
                throw Corrupted($"chunk.map.json entry {entry.ChunkId} references unknown file id {entry.FileId}.");
            }
        }
    }

    private void LoadVectors(int dimension, IReadOnlyList<ChunkMapEntry> chunkEntries, ReadOnlyMemory<float> vectors)
    {
        for (int i = 0; i < chunkEntries.Count; i++)
        {
            ChunkMapEntry entry = chunkEntries[i];
            ReadOnlyMemory<float> vector = vectors.Slice(i * dimension, dimension);
            // Snippet is intentionally empty: text is hydrated lazily via IChunkTextReader (stage 3).
            _vectorStore.Upsert(entry.ChunkId, entry.FileId, entry.PageNumber, string.Empty, vector);
        }
    }

    private static InvalidOperationException SessionNotOpen() =>
        new("The index session is not open. Call OpenAsync first.");

    private static IndexCorruptedException Corrupted(string details) =>
        new($"The index is corrupted: {details} Run 'vectra doctor' for diagnostics.");
}
