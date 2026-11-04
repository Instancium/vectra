#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RerankerContractTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "vectra-rerank-tests", Guid.NewGuid().ToString("N"));
    private readonly IVectraEngine _engine;

    public RerankerContractTests()
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
    public async Task SearchAsync_WithoutReranker_FinalScoreEqualsVectorScore()
    {
        var col = SetupCollection();
        await col.AddAsync(new VectorEntry(1, new float[] { 1f, 0f }, "payload"));

        var hits = await col.SearchAsync(new SearchQuery(new float[] { 1f, 0f }));

        Assert.Single(hits);
        Assert.Equal(hits[0].VectorScore, hits[0].FinalScore);
    }

    [Fact]
    public async Task SearchAsync_WithReranker_ShouldUpdateFinalScore()
    {
        var col = SetupCollection();
        await col.AddAsync(new VectorEntry(1, new float[] { 1f, 0f }, "payload"));

        var hits = await col.SearchAsync(new SearchQuery(new float[] { 1f, 0f }), new SearchOptions(reranker: new StubBoostReranker()));

        Assert.Single(hits);
        Assert.NotEqual(hits[0].VectorScore, hits[0].FinalScore);
        Assert.Equal(hits[0].VectorScore + 0.5f, hits[0].FinalScore);
    }

    [Fact]
    public async Task SearchAsync_WithMaliciousReranker_ShouldThrowInvalidOperationException()
    {
        var col = SetupCollection();
        await col.AddAsync(new VectorEntry(1, new float[] { 1f, 0f }, "payload"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await col.SearchAsync(new SearchQuery(new float[] { 1f, 0f }), new SearchOptions(reranker: new MaliciousReranker())));
    }

    private IVectraCollection SetupCollection()
    {
        return _engine.GetOrOpenCollection("rerank_col", new CollectionOptions(2, SimilarityMetric.DotProduct, "m1"));
    }

    private class StubBoostReranker : IReranker
    {
        public Task<IReadOnlyList<VectorSearchHit>> RerankAsync(SearchQuery query, IReadOnlyList<VectorSearchHit> candidates, CancellationToken cancellationToken = default)
        {
            var result = candidates.Select(c => new VectorSearchHit(c.VectorId, c.VectorScore, c.VectorScore + 0.5f, c.Payload, c.Diagnostics)).ToList();
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(result);
        }
    }

    private class MaliciousReranker : IReranker
    {
        public Task<IReadOnlyList<VectorSearchHit>> RerankAsync(SearchQuery query, IReadOnlyList<VectorSearchHit> candidates, CancellationToken cancellationToken = default)
        {
            var result = candidates.ToList();
            result.Add(new VectorSearchHit(999, 1f, 1f, "Malicious Payload"));
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(result);
        }
    }
}