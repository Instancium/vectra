#nullable enable
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
        ChunkRecord[] chunks = { CreateChunk(1, dimension: 3) };
        Assert.Throws<ArgumentException>(() => new IndexWriteRequest(CreateManifest(), chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldThrow_OnDuplicateChunkId()
    {
        ChunkRecord[] chunks = { CreateChunk(1), CreateChunk(1) };
        Assert.Throws<ArgumentException>(() => new IndexWriteRequest(CreateManifest(chunkCount: 2), chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldThrow_OnManifestCountMismatch()
    {
        ChunkRecord[] chunks = { CreateChunk(1) };
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new IndexWriteRequest(CreateManifest(chunkCount: 5), chunks));
    }

    [Fact]
    public void IndexWriteRequest_ShouldAcceptValidRequest()
    {
        ChunkRecord[] chunks = { CreateChunk(1) };
        var request = new IndexWriteRequest(CreateManifest(), chunks);
        Assert.Single(request.Chunks);
        Assert.Same(request.Manifest, request.Manifest);
    }

    private static DiskIndexManifest CreateManifest(int dimension = 4, int fileCount = 1, int chunkCount = 1) =>
        new(DiskIndexManifest.CurrentVersion, dimension, "stub-model", new[] { "pdf" }, Timestamp, Timestamp, fileCount, chunkCount);

    private static ChunkRecord CreateChunk(long chunkId, int dimension = 4) =>
        new(chunkId, 1, 1, "chunk " + chunkId, Enumerable.Repeat(0.1f, dimension).ToArray());
}

