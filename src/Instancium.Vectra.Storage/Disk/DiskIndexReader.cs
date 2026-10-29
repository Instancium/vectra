namespace Instancium.Vectra.Storage.Disk;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Default <see cref="IIndexReader"/> implementation. Stateless and safe for concurrent use.
/// </summary>
/// <remarks>
/// The reader validates format sanity only (JSON shape, byte alignment, byte ranges).
/// Cross-artifact consistency is enforced by <see cref="IVectraIndexSession"/> at open time
/// and by doctor (stage 6) on demand.
/// </remarks>
public sealed class DiskIndexReader : IIndexReader
{
    /// <inheritdoc />
    public async Task<DiskIndexManifest> ReadManifestAsync(string indexDirectory, CancellationToken cancellationToken = default)
    {
        byte[] utf8 = await ReadArtifactAsync(indexDirectory, IndexArtifactNames.Manifest, cancellationToken)
            .ConfigureAwait(false);
        ManifestDto dto = DiskIndexJson.DeserializeFromUtf8Bytes<ManifestDto>(utf8);
        return new DiskIndexManifest(
            dto.Version,
            dto.Dimension,
            dto.ModelId,
            dto.Formats,
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.FileCount,
            dto.ChunkCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileState>> ReadFileStatesAsync(string indexDirectory, CancellationToken cancellationToken = default)
    {
        byte[] utf8 = await ReadArtifactAsync(indexDirectory, IndexArtifactNames.FileState, cancellationToken)
            .ConfigureAwait(false);
        SortedDictionary<long, FileStateDto> dtos =
            DiskIndexJson.DeserializeFromUtf8Bytes<SortedDictionary<long, FileStateDto>>(utf8);
        return dtos
            .Select(pair => new FileState(
                pair.Key,
                pair.Value.Path,
                pair.Value.Sha256,
                pair.Value.SizeBytes,
                pair.Value.Format,
                pair.Value.ChunkIds,
                pair.Value.IndexedAt,
                pair.Value.Status))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkMapEntry>> ReadChunkMapAsync(string indexDirectory, CancellationToken cancellationToken = default)
    {
        byte[] utf8 = await ReadArtifactAsync(indexDirectory, IndexArtifactNames.ChunkMap, cancellationToken)
            .ConfigureAwait(false);
        ChunkMapEntryDto[] dtos = DiskIndexJson.DeserializeFromUtf8Bytes<ChunkMapEntryDto[]>(utf8);
        return dtos
            .Select(dto => new ChunkMapEntry(dto.ChunkId, dto.FileId, dto.PageNumber, dto.Offset, dto.Length, dto.IsActive))
            .OrderBy(entry => entry.ChunkId)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> ReadVectorsAsync(string indexDirectory, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadArtifactAsync(indexDirectory, IndexArtifactNames.Vectors, cancellationToken)
            .ConfigureAwait(false);
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException(
                $"'{IndexArtifactNames.Vectors}' size {bytes.Length} is not a multiple of {sizeof(float)}.");
        }

        var vectors = new float[bytes.Length / sizeof(float)];
        if (BitConverter.IsLittleEndian)
        {
            bytes.CopyTo(MemoryMarshal.AsBytes(vectors.AsSpan()));
        }
        else
        {
            for (int i = 0; i < vectors.Length; i++)
            {
                vectors[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
            }
        }

        return vectors;
    }

    /// <inheritdoc />
    public async ValueTask<string> ReadChunkTextAsync(string indexDirectory, long offset, int length, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        string path = ResolveArtifactPath(indexDirectory, IndexArtifactNames.Chunks);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (offset + length > stream.Length)
        {
            throw new InvalidDataException(
                $"Chunk text range [{offset}, {offset + length}) exceeds '{IndexArtifactNames.Chunks}' size {stream.Length}.");
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[length];
        int totalRead = 0;
        while (totalRead < length)
        {
            int partialRead = await stream
                .ReadAsync(buffer.AsMemory(totalRead, length - totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (partialRead == 0)
            {
                throw new InvalidDataException($"Unexpected end of '{IndexArtifactNames.Chunks}'.");
            }

            totalRead += partialRead;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<byte[]> ReadArtifactAsync(string indexDirectory, string artifactName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        string path = ResolveArtifactPath(indexDirectory, artifactName);
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException(
                $"Index artifact '{artifactName}' is missing in '{Path.GetFullPath(indexDirectory)}'.",
                path);
        }
    }

    private static string ResolveArtifactPath(string indexDirectory, string artifactName) =>
        Path.Combine(Path.GetFullPath(indexDirectory), artifactName);
}
