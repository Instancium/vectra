#nullable enable
namespace Instancium.Vectra.Tests.Unit.Engine;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

[Trait("Category", "Unit")]
public sealed class VectraEngineTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "vectra-engine-tests", Guid.NewGuid().ToString("N"));
    private readonly VectraOptions _options;

    public VectraEngineTests()
    {
        _options = new VectraOptions { BaseDirectory = _baseDir };
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetOrOpenCollection_ShouldCreateNewCollection_WhenItDoesNotExist()
    {
        await using IVectraEngine engine = VectraEngineFactory.Create(_options);
        var colOptions = new CollectionOptions(128, SimilarityMetric.Cosine, "test-model");

        IVectraCollection collection = engine.GetOrOpenCollection("col1", colOptions);

        Assert.Equal("col1", collection.Name);
        Assert.Equal(128, collection.Info.Dimension);
        Assert.Equal("test-model", collection.Info.ModelId);
    }

    [Fact]
    public async Task GetOrOpenCollection_ShouldReturnExistingCollection_WhenCalledTwice()
    {
        await using IVectraEngine engine = VectraEngineFactory.Create(_options);
        var colOptions = new CollectionOptions(128, SimilarityMetric.Cosine, "test-model");

        IVectraCollection col1 = engine.GetOrOpenCollection("col1", colOptions);
        IVectraCollection col2 = engine.GetOrOpenCollection("col1", colOptions);

        Assert.Same(col1, col2);
    }

    [Fact]
    public async Task GetOrOpenCollection_ShouldThrowVectraIncompatibleModelException_OnModelMismatch()
    {
        await using IVectraEngine engine = VectraEngineFactory.Create(_options);
        engine.GetOrOpenCollection("col1", new CollectionOptions(128, SimilarityMetric.Cosine, "model-A"));

        Assert.Throws<VectraIncompatibleModelException>(() =>
            engine.GetOrOpenCollection("col1", new CollectionOptions(128, SimilarityMetric.Cosine, "model-B")));
    }

    [Fact]
    public async Task ListCollectionsAsync_ShouldReturnAllCreatedCollections()
    {
        await using IVectraEngine engine = VectraEngineFactory.Create(_options);
        engine.GetOrOpenCollection("colA", new CollectionOptions(10, SimilarityMetric.Cosine, "m1"));
        engine.GetOrOpenCollection("colB", new CollectionOptions(20, SimilarityMetric.DotProduct, "m2"));

        var list = await engine.ListCollectionsAsync();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.Name == "colA" && c.Dimension == 10);
        Assert.Contains(list, c => c.Name == "colB" && c.Dimension == 20);
    }

    [Fact]
    public async Task DeleteCollectionAsync_ShouldRemoveCollectionFromDiskAndCatalog()
    {
        await using IVectraEngine engine = VectraEngineFactory.Create(_options);
        engine.GetOrOpenCollection("col_to_delete", new CollectionOptions(10, SimilarityMetric.Cosine, "m1"));

        Assert.Single(await engine.ListCollectionsAsync());

        await engine.DeleteCollectionAsync("col_to_delete");

        Assert.Empty(await engine.ListCollectionsAsync());
        Assert.False(Directory.Exists(Path.Combine(_baseDir, "col_to_delete")));
    }
}