namespace Instancium.Vectra.Tests.Unit.Search;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InMemoryVectorStoreTests
{
    [Fact]
    public void InMemoryVectorStore_ShouldStoreAndRetrieveVectors()
    {
        var store = new InMemoryVectorStore();
        var vector = new[] { 0.1f, 0.2f, 0.3f };

        store.Upsert(10, 5, 2, "alpha", vector);
        store.Upsert(11, 7, 1, "beta", new[] { 0.4f, 0.5f, 0.6f });

        Assert.Equal(2, store.Count);
        Assert.Equal(3, store.Dimension);

        var span = store.GetVectorsSpan();
        Assert.Equal(6, span.Length);
        Assert.Equal(0.1f, span[0]);
        Assert.Equal(0.2f, span[1]);
        Assert.Equal(0.3f, span[2]);
        Assert.Equal(0.4f, span[3]);
        Assert.Equal(0.5f, span[4]);
        Assert.Equal(0.6f, span[5]);

        var metadata = store.GetMetadata();
        Assert.Equal(2, metadata.Count);
        Assert.Equal(10, metadata[0].ChunkId);
        Assert.Equal(5, metadata[0].FileId);
        Assert.Equal(2, metadata[0].PageNumber);
        Assert.Equal("alpha", metadata[0].Snippet);
    }

    [Fact]
    public void InMemoryVectorStore_ShouldReplaceExistingChunkOnUpsert()
    {
        var store = new InMemoryVectorStore();
        var original = new[] { 1f, 2f, 3f };
        var replacement = new[] { 9f, 8f, 7f };

        store.Upsert(1, 1, 1, "first", original);
        store.Upsert(1, 1, 2, "updated", replacement);

        Assert.Equal(1, store.Count);
        Assert.Equal(3, store.Dimension);
        Assert.Equal(2, store.GetMetadata()[0].PageNumber);
        Assert.Equal("updated", store.GetMetadata()[0].Snippet);

        var span = store.GetVectorsSpan();
        Assert.Equal(9f, span[0]);
        Assert.Equal(8f, span[1]);
        Assert.Equal(7f, span[2]);
    }
}
