// tests/Instancium.Vectra.Tests.Integration/Embeddings/InstantAIGateEmbeddingAdapterIntegrationTests.cs
namespace Instancium.Vectra.Tests.Integration.Embeddings;

using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Infrastructure.Transport;
using Instancium.Vectra.Tests.Integration.Support;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Integration")]
public class InstantAIGateEmbeddingAdapterIntegrationTests
{
    [GatewayFact]
    public async Task Adapter_ShouldReturnDeterministicCanonicalVectors_FromRealGateway()
    {
        string repoId = GatewayTestEnvironment.EmbeddingRepoId;

        IOptions<EmbeddingGatewayOptions> options = Options.Create(new EmbeddingGatewayOptions
        {
            Url = GatewayTestEnvironment.GatewayUrl,
            HubPath = "/hub/gateway",
            RepoId = repoId,
            ApiKey = GatewayTestEnvironment.ApiKey!
        });
        await using var transport = new SignalREmbeddingTransport(options, NullLogger<SignalREmbeddingTransport>.Instance);
        var adapter = new InstantAIGateEmbeddingAdapter(transport, options, NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        int dimension = await adapter.GetEmbeddingDimensionAsync(repoId);
        var details = await transport.GetActiveModelDetailsAsync();

        Assert.True(dimension > 0);
        Assert.Equal(repoId, details.RepoId, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(dimension, details.EmbeddingDimension);

        EmbeddingResponse first = await adapter.GenerateEmbeddingsAsync(repoId, new EmbeddingRequest("Determinism probe text", Normalize: true));
        EmbeddingResponse second = await adapter.GenerateEmbeddingsAsync(repoId, new EmbeddingRequest("Determinism probe text", Normalize: true));

        Assert.Equal(dimension, first.Embedding.Length);
        Assert.Equal(dimension, first.EmbeddingDimension);
        Assert.Equal(first.Embedding, second.Embedding);
        Assert.Equal(first.TokenCount, second.TokenCount);
    }
}