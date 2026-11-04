#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using Instancium.Vectra.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InMemoryVectorStoreTests
{
    [Fact]
    public void Store_ShouldExposeSnapshotSpanAndMetadata()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(10, 5, 2, "alpha", new[] { 0.1f, 0.2f, 0.3f });
        store.Upsert(11, 7, 1, "beta", new[] { 0.4f, 0.5f, 0.6f });

        Assert.Equal(2, store.Count);
        Assert.Equal(3, store.Dimension);

        var span = store.GetVectorsSpan();
        Assert.Equal(6, span.Length);
        Assert.Equal(0.1f, span[0]);
        Assert.Equal(0.6f, span[5]);

        var metadata = store.GetMetadata();
        Assert.Equal(2, metadata.Count);
        Assert.Equal(10, metadata[0].ChunkId);
        Assert.Equal(5, metadata[0].FileId);
        Assert.Equal("alpha", metadata[0].Payload);
    }

    [Fact]
    public void Store_ShouldReplaceExistingChunkOnUpsert()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "first", new[] { 1f, 2f, 3f });
        store.Upsert(1, 1, 2, "updated", new[] { 9f, 8f, 7f });

        Assert.Equal(1, store.Count);
        Assert.Equal(9f, store.GetVectorsSpan()[0]);
        Assert.Equal("updated", store.GetMetadata()[0].Payload);
        Assert.Equal(2, store.GetMetadata()[0].PageNumber);
    }

    [Fact]
    public void Store_ShouldThrowOnDimensionMismatch()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "a", new[] { 1f, 2f });
        Assert.Throws<ArgumentException>(() => store.Upsert(2, 1, 1, "b", new[] { 1f, 2f, 3f }));
    }

    [Fact]
    public void Store_ShouldRemoveExistingChunkAndReturnTrue()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "A", new[] { 1f, 2f });
        store.Upsert(2, 1, 1, "B", new[] { 3f, 4f });

        bool removed = store.Remove(1);

        Assert.True(removed);
        Assert.Equal(1, store.Count);
        Assert.Equal(2, store.GetMetadata()[0].ChunkId);
        Assert.Equal(3f, store.GetVectorsSpan()[0]);
    }

    [Fact]
    public void Store_ShouldReturnFalseWhenRemovingNonExistentChunk()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "A", new[] { 1f, 2f });

        bool removed = store.Remove(999);

        Assert.False(removed);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Store_ShouldGetPayloadForExistingChunk()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "PayloadA", new[] { 1f, 2f });

        string? payload = store.GetPayload(1);

        Assert.Equal("PayloadA", payload);
    }
}

