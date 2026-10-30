// tests/Instancium.Vectra.Tests.Integration/Embeddings/InstantAIGateEmbeddingAdapterIntegrationTests.cs
namespace Instancium.Vectra.Tests.Integration.Embeddings;

using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Infrastructure.Transport;
using Instancium.Vectra.Tests.Integration.Support;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
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
        IReadOnlyList<NativeModelDetails> loadedModels = await transport.GetLoadedModelsDetailsAsync();
        NativeModelDetails? target = loadedModels.FirstOrDefault(m => string.Equals(m.RepoId, repoId, StringComparison.OrdinalIgnoreCase))
            ?? loadedModels.FirstOrDefault(m => m.IsEmbedding);

        Assert.True(dimension > 0);
        Assert.NotNull(target);
        Assert.Equal(dimension, target.EmbeddingDimension);
        Assert.True(target.IsEmbedding || string.Equals(target.RepoId, repoId, StringComparison.OrdinalIgnoreCase));

        EmbeddingResponse first = await adapter.GenerateEmbeddingsAsync(repoId, new EmbeddingRequest("Determinism probe text", Normalize: true));
        EmbeddingResponse second = await adapter.GenerateEmbeddingsAsync(repoId, new EmbeddingRequest("Determinism probe text", Normalize: true));

        Assert.Equal(dimension, first.Embedding.Length);
        Assert.Equal(dimension, first.EmbeddingDimension);
        Assert.Equal(first.Embedding, second.Embedding);
        Assert.Equal(first.TokenCount, second.TokenCount);
    }
}