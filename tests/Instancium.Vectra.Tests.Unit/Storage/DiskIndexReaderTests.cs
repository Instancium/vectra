#nullable enable
namespace Instancium.Vectra.Tests.Unit.Storage;

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
        Path.Combine(Path.GetTempPath(), "vectra-reader-v2", Guid.NewGuid().ToString("N")));
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
        Assert.Equal(request.Manifest.Dimension, manifest.Dimension);
        Assert.Equal(request.Manifest.ModelId, manifest.ModelId);
        Assert.Equal(request.Manifest.ChunkCount, manifest.ChunkCount);

        IReadOnlyList<ChunkMapEntry> chunkMap = await _reader.ReadChunkMapAsync(indexDirectory);
        Assert.Equal(request.Chunks.Count, chunkMap.Count);
        for (int i = 0; i < request.Chunks.Count; i++)
        {
            ChunkRecord expected = request.Chunks[i];
            ChunkMapEntry actual = chunkMap[i];
            Assert.Equal(expected.ChunkId, actual.ChunkId);
            Assert.Equal(expected.FileId, actual.FileId);
            Assert.Equal(expected.PageNumber, actual.PageNumber);
            Assert.True(actual.IsActive);
            string text = await _reader.ReadChunkTextAsync(indexDirectory, actual.Offset, actual.Length);
            Assert.Equal(expected.Text, text);
        }

        ReadOnlyMemory<float> vectors = await _reader.ReadVectorsAsync(indexDirectory);
        Assert.Equal(request.Chunks.Count * Dimension, vectors.Length);
        for (int i = 0; i < request.Chunks.Count; i++)
        {
            Assert.True(request.Chunks[i].Vector.Span.SequenceEqual(vectors.Span.Slice(i * Dimension, Dimension)));
        }
    }

    [Fact]
    public async Task DiskIndexReader_ShouldThrowIndexCorruptedException_OnDimensionMismatch()
    {
        string indexDirectory = PrepareIndexDirectory();
        await _writer.WriteAsync(CreateSampleRequest(), indexDirectory);
        string vectorsPath = Path.Combine(indexDirectory, IndexArtifactNames.Vectors);
        await using (var stream = new FileStream(vectorsPath, FileMode.Append, FileAccess.Write))
        {
            await stream.WriteAsync(new byte[sizeof(float)]);
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
            DiskIndexManifest.CurrentVersion, Dimension, "stub-model", new[] { "pdf" },
            CreatedAt, UpdatedAt, fileCount: 2, chunkCount: 3);
        ChunkRecord[] chunks =
        {
            new(1, 1, 1, "chunk text 1", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }),
            new(2, 1, 2, "chunk text 2", new float[] { 0.5f, 0.6f, 0.7f, 0.8f }),
            new(3, 2, 1, "chunk text 3", new float[] { 0.9f, 1.0f, 1.1f, 1.2f })
        };
        return new IndexWriteRequest(manifest, chunks);
    }
}

