namespace Instancium.Vectra.Tests.Unit.Storage;

using System.Buffers.Binary;
using System.Text;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DiskIndexWriterTests : IDisposable
{
    private const int Dimension = 4;
    private const int OffsetRecordSize = sizeof(long) + sizeof(int);
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-stage1", Guid.NewGuid().ToString("N")));
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
    public async Task DiskIndexWriter_ShouldWriteAllSixFiles()
    {
        string indexDirectory = PrepareIndexDirectory();

        await _writer.WriteAsync(CreateRequest(), indexDirectory);

        foreach (string artifact in IndexArtifactNames.All)
        {
            Assert.True(File.Exists(Path.Combine(indexDirectory, artifact)), $"Missing artifact: {artifact}");
        }
        Assert.DoesNotContain(
            Directory.EnumerateFiles(indexDirectory),
            file => file.EndsWith(".tmp", StringComparison.Ordinal));

        using var manifest = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(indexDirectory, IndexArtifactNames.Manifest)));

        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(Dimension, root.GetProperty("dimension").GetInt32());
        Assert.Equal("stub-model", root.GetProperty("modelId").GetString());
        Assert.Equal("pdf", root.GetProperty("formats")[0].GetString());
        Assert.Equal(2, root.GetProperty("fileCount").GetInt32());
        Assert.Equal(3, root.GetProperty("chunkCount").GetInt32());
        Assert.Equal(CreatedAt, root.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(UpdatedAt, root.GetProperty("updatedAt").GetDateTimeOffset());

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
            int length = BinaryPrimitives.ReadInt32LittleEndian(
                offsets.AsSpan(i * OffsetRecordSize + sizeof(long)));
            Assert.Equal($"chunk text {i + 1}", Encoding.UTF8.GetString(texts, checked((int)offset), length));
        }

        using var chunkMap = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(indexDirectory, IndexArtifactNames.ChunkMap)));

        Assert.Equal(3, chunkMap.RootElement.GetArrayLength());
        var first = chunkMap.RootElement[0];
        Assert.Equal(1, first.GetProperty("chunkId").GetInt64());
        Assert.Equal(1, first.GetProperty("fileId").GetInt64());
        Assert.Equal(1, first.GetProperty("pageNumber").GetInt32());
        Assert.Equal(0, first.GetProperty("offset").GetInt64());
        Assert.Equal("chunk text 1".Length, first.GetProperty("length").GetInt32());
        Assert.True(first.GetProperty("isActive").GetBoolean());

        using var fileState = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(indexDirectory, IndexArtifactNames.FileState)));

        var fileOne = fileState.RootElement.GetProperty("1");
        Assert.Equal("docs/manual.pdf", fileOne.GetProperty("path").GetString());
        Assert.Equal("aaaa", fileOne.GetProperty("sha256").GetString());
        Assert.Equal(100, fileOne.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("pdf", fileOne.GetProperty("format").GetString());
        Assert.Equal("Indexed", fileOne.GetProperty("status").GetString());
        Assert.Equal(2, fileOne.GetProperty("chunkIds").GetArrayLength());
        Assert.True(fileState.RootElement.TryGetProperty("2", out _));
    }

    [Fact]
    public async Task DiskIndexWriter_ShouldBeDeterministic()
    {
        string firstDirectory = PrepareIndexDirectory("run-a");
        string secondDirectory = PrepareIndexDirectory("run-b");

        await _writer.WriteAsync(CreateRequest(), firstDirectory);
        await _writer.WriteAsync(
            CreateRequest(reverseChunkOrder: true, reverseFileOrder: true),
            secondDirectory);

        foreach (string artifact in IndexArtifactNames.All)
        {
            byte[] left = File.ReadAllBytes(Path.Combine(firstDirectory, artifact));
            byte[] right = File.ReadAllBytes(Path.Combine(secondDirectory, artifact));
            Assert.True(left.SequenceEqual(right), $"Artifact '{artifact}' is not byte-identical across runs.");
        }
    }

    [Fact]
    public async Task DiskIndexWriter_ShouldReplacePreviousIndex()
    {
        string indexDirectory = PrepareIndexDirectory();

        await _writer.WriteAsync(CreateRequest(), indexDirectory);
        await _writer.WriteAsync(CreateSingleChunkRequest(), indexDirectory);

        using var manifest = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(indexDirectory, IndexArtifactNames.Manifest)));
        Assert.Equal(1, manifest.RootElement.GetProperty("chunkCount").GetInt32());

        byte[] vectors = File.ReadAllBytes(Path.Combine(indexDirectory, IndexArtifactNames.Vectors));
        Assert.Equal(Dimension * sizeof(float), vectors.Length);
    }

    [Fact]
    public async Task DiskIndexWriter_ShouldThrow_WhenIndexDirectoryDoesNotExist()
    {
        string missingDirectory = Path.Combine(_root, "no", "such", "dir");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _writer.WriteAsync(CreateRequest(), missingDirectory));
    }

    private string PrepareIndexDirectory(string? name = null)
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(
            _root,
            name is null ? null : Path.Combine(_root, name));
        _resolver.EnsureDirectoryStructure(indexDirectory);
        return indexDirectory;
    }

    private static IndexWriteRequest CreateRequest(bool reverseChunkOrder = false, bool reverseFileOrder = false)
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

        return new IndexWriteRequest(
            manifest,
            reverseFileOrder ? files.Reverse().ToArray() : files,
            reverseChunkOrder ? chunks.Reverse().ToArray() : chunks);
    }

    private static IndexWriteRequest CreateSingleChunkRequest()
    {
        var manifest = new DiskIndexManifest(
            version: DiskIndexManifest.CurrentVersion,
            dimension: Dimension,
            modelId: "stub-model",
            formats: new[] { "pdf" },
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            fileCount: 1,
            chunkCount: 1);

        FileState[] files =
        {
            new(1, "docs/manual.pdf", "aaaa", 100, "pdf", new long[] { 1 }, UpdatedAt, IndexFileStatus.Indexed)
        };

        ChunkRecord[] chunks =
        {
            new(1, 1, 1, "single chunk", new float[] { 1f, 0f, 0f, 0f })
        };

        return new IndexWriteRequest(manifest, files, chunks);
    }
}
