namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Core.Models;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IndexWriteRequestTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IndexWriteRequest_ShouldThrow_OnVectorDimensionMismatch()
    {
        DiskIndexManifest manifest = CreateManifest(dimension: 4, fileCount: 1, chunkCount: 1);
        FileState[] files = { CreateFile(1, chunkIds: new long[] { 1 }) };
        ChunkRecord[] chunks = { CreateChunk(1, 1, dimension: 3) };

        Assert.Throws<ArgumentException>(() => new IndexWriteRequest(manifest, files, chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldThrow_OnDuplicateChunkId()
    {
        DiskIndexManifest manifest = CreateManifest(fileCount: 1, chunkCount: 2);
        FileState[] files = { CreateFile(1, chunkIds: new long[] { 1, 2 }) };
        ChunkRecord[] chunks = { CreateChunk(1, 1), CreateChunk(1, 1) };

        Assert.Throws<ArgumentException>(() => new IndexWriteRequest(manifest, files, chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldThrow_OnChunkReferencingUnknownFile()
    {
        DiskIndexManifest manifest = CreateManifest(fileCount: 1, chunkCount: 1);
        FileState[] files = { CreateFile(1, chunkIds: new long[] { 1 }) };
        ChunkRecord[] chunks = { CreateChunk(1, fileId: 99) };

        Assert.Throws<ArgumentException>(() => new IndexWriteRequest(manifest, files, chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldThrow_OnManifestCountMismatch()
    {
        DiskIndexManifest manifest = CreateManifest(fileCount: 1, chunkCount: 5);
        FileState[] files = { CreateFile(1, chunkIds: new long[] { 1 }) };
        ChunkRecord[] chunks = { CreateChunk(1, 1) };

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new IndexWriteRequest(manifest, files, chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldThrow_WhenFileChunkListIsInconsistent()
    {
        DiskIndexManifest manifest = CreateManifest(fileCount: 1, chunkCount: 1);
        FileState[] files = { CreateFile(1, chunkIds: new long[] { 1, 2 }) };
        ChunkRecord[] chunks = { CreateChunk(1, 1) };

        Assert.Throws<ArgumentException>(() => new IndexWriteRequest(manifest, files, chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldAcceptValidRequest()
    {
        DiskIndexManifest manifest = CreateManifest(fileCount: 1, chunkCount: 1);
        FileState[] files = { CreateFile(1, chunkIds: new long[] { 1 }) };
        ChunkRecord[] chunks = { CreateChunk(1, 1) };

        var request = new IndexWriteRequest(manifest, files, chunks);

        Assert.Same(manifest, request.Manifest);
        Assert.Single(request.Files);
        Assert.Single(request.Chunks);
    }

    private static DiskIndexManifest CreateManifest(int dimension = 4, int fileCount = 1, int chunkCount = 1) =>
        new(
            DiskIndexManifest.CurrentVersion,
            dimension,
            "stub-model",
            new[] { "pdf" },
            Timestamp,
            Timestamp,
            fileCount,
            chunkCount);

    private static FileState CreateFile(long fileId, params long[] chunkIds) =>
        new(fileId, $"docs/file{fileId}.pdf", "hash", 100, "pdf", chunkIds, Timestamp, IndexFileStatus.Indexed);

    private static ChunkRecord CreateChunk(long chunkId, long fileId, int dimension = 4) =>
        new(chunkId, fileId, 1, $"chunk {chunkId}", Enumerable.Repeat(0.1f, dimension).ToArray());
}
