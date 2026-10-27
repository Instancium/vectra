namespace Instancium.Vectra.Tests.Integration.Embeddings;

using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Integration")]
public class InstantAIGateEmbeddingAdapterIntegrationTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldReturnValidVector_FromRealGateway()
    {
        // Arrange
        var options = Options.Create(new EmbeddingGatewayOptions
        {
            Url = "http://localhost:5001",
            HubPath = "/hub/gateway",
            RepoId = "qwen3-embedding-0.6b",
            ApiKey = "ChangeMeSuperSecureAdminApiKey123"
        });

        var transport = new SignalREmbeddingTransport(
            options,
            NullLogger<SignalREmbeddingTransport>.Instance);

        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act
        var dimension = await adapter.GetEmbeddingDimensionAsync(options.Value.RepoId);
        var response = await adapter.GenerateEmbeddingsAsync(
            options.Value.RepoId,
            new EmbeddingRequest("Test text for embedding"));

        // Assert
        Assert.True(dimension > 0);
        Assert.NotNull(response.Embedding);
        Assert.Equal(dimension, response.Embedding.Length);
        Assert.Equal(dimension, response.EmbeddingDimension);
    }
}