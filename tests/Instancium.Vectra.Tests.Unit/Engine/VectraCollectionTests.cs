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
public sealed class VectraCollectionTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "vectra-col-tests", Guid.NewGuid().ToString("N"));
    private readonly IVectraEngine _engine;

    public VectraCollectionTests()
    {
        Directory.CreateDirectory(_baseDir);
        _engine = VectraEngineFactory.Create(new VectraOptions { BaseDirectory = _baseDir });
    }

    public void Dispose()
    {
        _engine.DisposeAsync().GetAwaiter().GetResult();
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    [Fact]
    public async Task RoundTrip_AddSearchGetPayload_ShouldWorkCorrectly()
    {
        var col = _engine.GetOrOpenCollection("rt", new CollectionOptions(3, SimilarityMetric.DotProduct, "m1"));

        long id1 = await col.AddAsync(new VectorEntry(null, new float[] { 1f, 0f, 0f }, "Doc 1"));
        long id2 = await col.AddAsync(new VectorEntry(null, new float[] { 0.9f, 0.1f, 0f }, "Doc 2"));

        var hits = await col.SearchAsync(new SearchQuery(new float[] { 1f, 0f, 0f }), new SearchOptions(topK: 1));

        Assert.Single(hits);
        Assert.Equal(id1, hits[0].VectorId);
        Assert.Equal("Doc 1", hits[0].Payload); // Payload is hydrated
    }

    [Fact]
    public async Task AddAsync_ShouldNormalizeCosineDeterministically()
    {
        var col = _engine.GetOrOpenCollection("cosine_col", new CollectionOptions(2, SimilarityMetric.Cosine, "m1"));

        // Unnormalized vector
        long id = await col.AddAsync(new VectorEntry(null, new float[] { 3f, 4f }, "Test"));

        // Query with exact normalized vector [0.6, 0.8]
        var hits = await col.SearchAsync(new SearchQuery(new float[] { 0.6f, 0.8f }));

        Assert.Single(hits);
        Assert.True(hits[0].VectorScore > 0.999f);
    }

    [Fact]
    public async Task AddAsync_ShouldThrowDimensionMismatch()
    {
        var col = _engine.GetOrOpenCollection("dim_col", new CollectionOptions(4, SimilarityMetric.Cosine, "m1"));

        await Assert.ThrowsAsync<VectraDimensionMismatchException>(async () =>
            await col.AddAsync(new VectorEntry(null, new float[] { 1f, 2f }, "Err")));
    }

    [Fact]
    public async Task RemoveAsync_ShouldSoftDeleteAndRemoveFromSearchResults()
    {
        var col = _engine.GetOrOpenCollection("rem_col", new CollectionOptions(2, SimilarityMetric.DotProduct, "m1"));

        long id1 = await col.AddAsync(new VectorEntry(null, new float[] { 1f, 1f }, "Keep"));
        long id2 = await col.AddAsync(new VectorEntry(null, new float[] { 1f, 1f }, "Delete"));

        bool removed = await col.RemoveAsync(id2);

        Assert.True(removed);
        var hits = await col.SearchAsync(new SearchQuery(new float[] { 1f, 1f }));

        Assert.Single(hits);
        Assert.Equal(id1, hits[0].VectorId);
    }
}
