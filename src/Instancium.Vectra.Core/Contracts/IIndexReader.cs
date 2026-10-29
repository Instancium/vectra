namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Reads the on-disk index artifacts produced by <see cref="IIndexWriter"/>.
/// Implementations are stateless: every method receives the index directory,
/// so a single instance may be used concurrently.
/// </summary>
public interface IIndexReader
{
    /// <summary>Reads <c>manifest.json</c>.</summary>
    /// <exception cref="FileNotFoundException">Thrown when the artifact is missing.</exception>
    Task<DiskIndexManifest> ReadManifestAsync(string indexDirectory, CancellationToken cancellationToken = default);

    /// <summary>Reads <c>file.state.json</c> ordered by file id.</summary>
    /// <exception cref="FileNotFoundException">Thrown when the artifact is missing.</exception>
    Task<IReadOnlyList<FileState>> ReadFileStatesAsync(string indexDirectory, CancellationToken cancellationToken = default);

    /// <summary>Reads <c>chunk.map.json</c> ordered by chunk id.</summary>
    /// <exception cref="FileNotFoundException">Thrown when the artifact is missing.</exception>
    Task<IReadOnlyList<ChunkMapEntry>> ReadChunkMapAsync(string indexDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <c>vectors.bin</c> as one flat float array.
    /// Vector <c>i</c> occupies <c>[i * dimension, (i + 1) * dimension)</c> and corresponds
    /// to the i-th entry of the chunk map.
    /// </summary>
    /// <exception cref="FileNotFoundException">Thrown when the artifact is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file size is not a multiple of four bytes.</exception>
    Task<ReadOnlyMemory<float>> ReadVectorsAsync(string indexDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one chunk text from <c>chunks.bin</c> by byte range (lazy access, TZ-2 stage 2).
    /// The offset and length are taken from the corresponding <see cref="ChunkMapEntry"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">Thrown when <c>chunks.bin</c> is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the requested range exceeds the file size.</exception>
    ValueTask<string> ReadChunkTextAsync(string indexDirectory, long offset, int length, CancellationToken cancellationToken = default);
}
