namespace Instancium.Vectra.Tests.Unit.Storage;

using System.Buffers.Binary;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DiskIndexReaderTests : IDisposable
{
    private const int Dimension = 4;
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-reader-tests", Guid.NewGuid().ToString("N")));
    private readonly InstanciumDirectoryResolver _resolver = new();
    private readonly DiskIndexWriter _writer = new();
    private readonly DiskIndexReader _reader = new();

    public DiskIndexReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task DiskIndexReader_ShouldLoadWhatWriterProduced()
    {
        string indexDirectory = PrepareIndexDirectory();
        IndexWriteRequest request = CreateSampleRequest();
        await _writer.WriteAsync(request, indexDirectory);

        DiskIndexManifest manifest = await _reader.ReadManifestAsync(indexDirectory);
        Assert.Equal(request.Manifest.Version, manifest.Version);
        Assert.Equal(request.Manifest.Dimension, manifest.Dimension);
        Assert.Equal(request.Manifest.ModelId, manifest.ModelId);
        Assert.Equal(request.Manifest.Formats, manifest.Formats);
        Assert.Equal(request.Manifest.CreatedAt, manifest.CreatedAt);
        Assert.Equal(request.Manifest.UpdatedAt, manifest.UpdatedAt);
        Assert.Equal(request.Manifest.FileCount, manifest.FileCount);
        Assert.Equal(request.Manifest.ChunkCount, manifest.ChunkCount);

        IReadOnlyList<FileState> fileStates = await _reader.ReadFileStatesAsync(indexDirectory);
        Assert.Equal(request.Files.Count, fileStates.Count);
        for (int i = 0; i < request.Files.Count; i++)
        {
            FileState expected = request.Files[i];
            FileState actual = fileStates[i];
            Assert.Equal(expected.FileId, actual.FileId);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Sha256, actual.Sha256);
            Assert.Equal(expected.SizeBytes, actual.SizeBytes);
            Assert.Equal(expected.Format, actual.Format);
            Assert.Equal(expected.ChunkIds, actual.ChunkIds);
            Assert.Equal(expected.IndexedAt, actual.IndexedAt);
            Assert.Equal(expected.Status, actual.Status);
        }

        IReadOnlyList<ChunkMapEntry> chunkMap = await _reader.ReadChunkMapAsync(indexDirectory);
        Assert.Equal(request.Chunks.Count, chunkMap.Count);
        for (int i = 0; i < request.Chunks.Count; i++)
        {
            ChunkRecord expectedChunk = request.Chunks[i];
            ChunkMapEntry actualEntry = chunkMap[i];
            Assert.Equal(expectedChunk.ChunkId, actualEntry.ChunkId);
            Assert.Equal(expectedChunk.FileId, actualEntry.FileId);
            Assert.Equal(expectedChunk.PageNumber, actualEntry.PageNumber);
            Assert.True(actualEntry.IsActive);

            string chunkText = await _reader.ReadChunkTextAsync(indexDirectory, actualEntry.Offset, actualEntry.Length);
            Assert.Equal(expectedChunk.Text, chunkText);
        }

        ReadOnlyMemory<float> vectors = await _reader.ReadVectorsAsync(indexDirectory);
        Assert.Equal(request.Chunks.Count * Dimension, vectors.Length);

        ReadOnlySpan<float> vectorSpan = vectors.Span;
        for (int i = 0; i < request.Chunks.Count; i++)
        {
            ReadOnlySpan<float> expectedVector = request.Chunks[i].Vector.Span;
            ReadOnlySpan<float> actualVector = vectorSpan.Slice(i * Dimension, Dimension);
            Assert.True(expectedVector.SequenceEqual(actualVector));
        }
    }

    [Fact]
    public async Task DiskIndexReader_ShouldThrowIndexCorruptedException_OnDimensionMismatch()
    {
        string indexDirectory = PrepareIndexDirectory();
        IndexWriteRequest request = CreateSampleRequest();
        await _writer.WriteAsync(request, indexDirectory);

        string vectorsPath = Path.Combine(indexDirectory, IndexArtifactNames.Vectors);
        byte[] extraBytes = new byte[sizeof(float)];
        await using (var stream = new FileStream(vectorsPath, FileMode.Append, FileAccess.Write))
        {
            await stream.WriteAsync(extraBytes);
        }

        await Assert.ThrowsAsync<IndexCorruptedException>(() => _reader.ReadVectorsAsync(indexDirectory));
    }

    private string PrepareIndexDirectory()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root, Path.Combine(_root, "idx"));
        _resolver.EnsureDirectoryStructure(indexDirectory);
        return indexDirectory;
    }

    private static IndexWriteRequest CreateSampleRequest()
    {
        var manifest = new DiskIndexManifest(
            version: DiskIndexManifest.CurrentVersion,
            dimension: Dimension,
            modelId: "stub-model",
            formats: new[] { "pdf" },
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            fileCount: 2,
            chunkCount: 3);

        FileState[] files =
        {
            new(1, "docs/manual.pdf", "aaaa", 100, "pdf", new long[] { 1, 2 }, CreatedAt, IndexFileStatus.Indexed),
            new(2, "docs/guide.pdf", "bbbb", 200, "pdf", new long[] { 3 }, CreatedAt, IndexFileStatus.Indexed)
        };

        ChunkRecord[] chunks =
        {
            new(1, 1, 1, "chunk text 1", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }),
            new(2, 1, 2, "chunk text 2", new float[] { 0.5f, 0.6f, 0.7f, 0.8f }),
            new(3, 2, 1, "chunk text 3", new float[] { 0.9f, 1.0f, 1.1f, 1.2f })
        };

        return new IndexWriteRequest(manifest, files, chunks);
    }
}
