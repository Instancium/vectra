#nullable enable
namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using System.Buffers.Binary;
using System.Text;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DiskIndexWriterTests : IDisposable
{
    private const int Dimension = 4;
    private const int OffsetRecordSize = sizeof(long) + sizeof(int);
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-writer-v2", Guid.NewGuid().ToString("N")));
    private readonly InstanciumDirectoryResolver _resolver = new();
    private readonly DiskIndexWriter _writer = new();

    public DiskIndexWriterTests()
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
    public async Task DiskIndexWriter_ShouldWriteAllFiveArtifacts_AndNoFileState()
    {
        string indexDirectory = PrepareIndexDirectory();
        await _writer.WriteAsync(CreateRequest(), indexDirectory);

        foreach (string artifact in IndexArtifactNames.All)
        {
            Assert.True(File.Exists(Path.Combine(indexDirectory, artifact)), $"Missing artifact: {artifact}");
        }

        Assert.False(File.Exists(Path.Combine(indexDirectory, "file.state.json")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(indexDirectory),
            file => file.EndsWith(".tmp", StringComparison.Ordinal));

        using var manifest = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(indexDirectory, IndexArtifactNames.Manifest)));
        Assert.Equal(DiskIndexManifest.CurrentVersion, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(Dimension, manifest.RootElement.GetProperty("dimension").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("fileCount").GetInt32());
        Assert.Equal(3, manifest.RootElement.GetProperty("chunkCount").GetInt32());

        byte[] vectors = File.ReadAllBytes(Path.Combine(indexDirectory, IndexArtifactNames.Vectors));
        Assert.Equal(3 * Dimension * sizeof(float), vectors.Length);
        Assert.Equal(0.1f, BinaryPrimitives.ReadSingleLittleEndian(vectors.AsSpan(0)));
        Assert.Equal(1.2f, BinaryPrimitives.ReadSingleLittleEndian(vectors.AsSpan(vectors.Length - sizeof(float))));

        byte[] texts = File.ReadAllBytes(Path.Combine(indexDirectory, IndexArtifactNames.Chunks));
        byte[] offsets = File.ReadAllBytes(Path.Combine(indexDirectory, IndexArtifactNames.Offsets));
        Assert.Equal(3 * OffsetRecordSize, offsets.Length);
        for (int i = 0; i < 3; i++)
        {
            long offset = BinaryPrimitives.ReadInt64LittleEndian(offsets.AsSpan(i * OffsetRecordSize));
            int length = BinaryPrimitives.ReadInt32LittleEndian(offsets.AsSpan(i * OffsetRecordSize + sizeof(long)));
            Assert.Equal("chunk text " + (i + 1), Encoding.UTF8.GetString(texts, checked((int)offset), length));
        }
    }

    [Fact]
    public async Task DiskIndexWriter_ShouldBeDeterministic()
    {
        string first = PrepareIndexDirectory("run-a");
        string second = PrepareIndexDirectory("run-b");
        await _writer.WriteAsync(CreateRequest(), first);
        await _writer.WriteAsync(CreateRequest(reverseChunkOrder: true), second);

        foreach (string artifact in IndexArtifactNames.All)
        {
            byte[] left = File.ReadAllBytes(Path.Combine(first, artifact));
            byte[] right = File.ReadAllBytes(Path.Combine(second, artifact));
            Assert.True(left.SequenceEqual(right), $"Artifact '{artifact}' is not byte-identical across runs.");
        }
    }

    [Fact]
    public async Task DiskIndexWriter_ShouldReplacePreviousIndex()
    {
        string indexDirectory = PrepareIndexDirectory();
        await _writer.WriteAsync(CreateRequest(), indexDirectory);
        await _writer.WriteAsync(CreateSingleChunkRequest(), indexDirectory);

        byte[] vectors = File.ReadAllBytes(Path.Combine(indexDirectory, IndexArtifactNames.Vectors));
        Assert.Equal(Dimension * sizeof(float), vectors.Length);
    }

    [Fact]
    public async Task DiskIndexWriter_ShouldThrow_WhenIndexDirectoryDoesNotExist()
    {
        string missing = Path.Combine(_root, "no", "such", "dir");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _writer.WriteAsync(CreateRequest(), missing));
    }

    private string PrepareIndexDirectory(string? name = null)
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(
            _root,
            name is null ? null : Path.Combine(_root, name));
        _resolver.EnsureDirectoryStructure(indexDirectory);
        return indexDirectory;
    }

    private static IndexWriteRequest CreateRequest(bool reverseChunkOrder = false)
    {
        ChunkRecord[] chunks = CreateChunks();
        return new IndexWriteRequest(
            CreateManifest(chunkCount: chunks.Length),
            reverseChunkOrder ? chunks.Reverse().ToArray() : chunks);
    }

    private static IndexWriteRequest CreateSingleChunkRequest() =>
        new(
            CreateManifest(fileCount: 1, chunkCount: 1),
            new[] { new ChunkRecord(1, 1, 1, "single chunk", new float[] { 1f, 0f, 0f, 0f }) });

    private static DiskIndexManifest CreateManifest(int fileCount = 2, int chunkCount = 3) =>
        new(DiskIndexManifest.CurrentVersion, Dimension, "stub-model", new[] { "pdf" }, CreatedAt, UpdatedAt, fileCount, chunkCount);

    private static ChunkRecord[] CreateChunks() => new[]
    {
        new ChunkRecord(1, 1, 1, "chunk text 1", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }),
        new ChunkRecord(2, 1, 2, "chunk text 2", new float[] { 0.5f, 0.6f, 0.7f, 0.8f }),
        new ChunkRecord(3, 2, 1, "chunk text 3", new float[] { 0.9f, 1.0f, 1.1f, 1.2f })
    };
}