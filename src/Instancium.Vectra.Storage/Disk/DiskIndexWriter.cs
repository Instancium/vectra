namespace Instancium.Vectra.Storage.Disk;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Default <see cref="IIndexWriter"/> implementation. Serializes an <see cref="IndexWriteRequest"/>
/// into the six index artifacts defined by TZ-2 section 3.2.
/// </summary>
public sealed class DiskIndexWriter : IIndexWriter
{
    private const int OffsetRecordSize = sizeof(long) + sizeof(int);

    public async Task WriteAsync(
        IndexWriteRequest request,
        string indexDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);

        string fullDirectory = Path.GetFullPath(indexDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Index directory '{fullDirectory}' does not exist. " +
                "Call IInstanciumDirectoryResolver.EnsureDirectoryStructure first.");
        }

        ChunkRecord[] chunks = request.Chunks.OrderBy(chunk => chunk.ChunkId).ToArray();
        FileState[] files = request.Files.OrderBy(file => file.FileId).ToArray();

        ChunkPayload payload = BuildChunkPayload(chunks);

        await AtomicFileWriter.WriteAllBytesAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.Chunks),
            payload.Texts,
            cancellationToken).ConfigureAwait(false);
        await AtomicFileWriter.WriteAllBytesAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.Offsets),
            payload.Offsets,
            cancellationToken).ConfigureAwait(false);
        await AtomicFileWriter.WriteAllBytesAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.Vectors),
            BuildVectorPayload(chunks, request.Manifest.Dimension),
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.ChunkMap),
            payload.ChunkMap,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.FileState),
            BuildFileState(files),
            cancellationToken).ConfigureAwait(false);

        var manifest = new ManifestDto(
            request.Manifest.Version,
            request.Manifest.Dimension,
            request.Manifest.ModelId,
            request.Manifest.Formats,
            request.Manifest.CreatedAt,
            request.Manifest.UpdatedAt,
            request.Manifest.FileCount,
            request.Manifest.ChunkCount);
        await WriteJsonAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.Manifest),
            manifest,
            cancellationToken).ConfigureAwait(false);
    }

    private static ChunkPayload BuildChunkPayload(IReadOnlyList<ChunkRecord> chunks)
    {
        var encoded = new byte[chunks.Count][];
        long totalLength = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            encoded[i] = Encoding.UTF8.GetBytes(chunks[i].Text);
            totalLength += encoded[i].Length;
        }

        var texts = new byte[checked((int)totalLength)];
        var offsets = new byte[chunks.Count * OffsetRecordSize];
        var map = new ChunkMapEntryDto[chunks.Count];

        long offset = 0;
        int cursor = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            encoded[i].CopyTo(texts, cursor);
            BinaryPrimitives.WriteInt64LittleEndian(offsets.AsSpan(i * OffsetRecordSize), offset);
            BinaryPrimitives.WriteInt32LittleEndian(
                offsets.AsSpan(i * OffsetRecordSize + sizeof(long)),
                encoded[i].Length);
            map[i] = new ChunkMapEntryDto(
                chunks[i].ChunkId,
                chunks[i].FileId,
                chunks[i].PageNumber,
                offset,
                encoded[i].Length,
                IsActive: chunks[i].IsActive);
            offset += encoded[i].Length;
            cursor += encoded[i].Length;
        }

        return new ChunkPayload(texts, offsets, map);
    }

    private static byte[] BuildVectorPayload(IReadOnlyList<ChunkRecord> chunks, int dimension)
    {
        int vectorBytes = dimension * sizeof(float);
        var payload = new byte[checked(chunks.Count * vectorBytes)];
        int cursor = 0;
        foreach (ChunkRecord chunk in chunks)
        {
            WriteLittleEndian(chunk.Vector.Span, payload.AsSpan(cursor, vectorBytes));
            cursor += vectorBytes;
        }
        return payload;
    }

    private static void WriteLittleEndian(ReadOnlySpan<float> source, Span<byte> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(source).CopyTo(destination);
            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                destination.Slice(i * sizeof(float), sizeof(float)),
                source[i]);
        }
    }

    private static SortedDictionary<long, FileStateDto> BuildFileState(IReadOnlyList<FileState> files)
    {
        var state = new SortedDictionary<long, FileStateDto>();
        foreach (FileState file in files)
        {
            state.Add(file.FileId, new FileStateDto(
                file.Path,
                file.Sha256,
                file.SizeBytes,
                file.Format,
                file.ChunkIds,
                file.IndexedAt,
                file.Status));
        }
        return state;
    }

    private static Task WriteJsonAtomicAsync<T>(string targetPath, T value, CancellationToken cancellationToken) =>
        AtomicFileWriter.WriteAllBytesAtomicAsync(targetPath, DiskIndexJson.SerializeToUtf8Bytes(value), cancellationToken);

    private sealed record ChunkPayload(byte[] Texts, byte[] Offsets, ChunkMapEntryDto[] ChunkMap);
}
