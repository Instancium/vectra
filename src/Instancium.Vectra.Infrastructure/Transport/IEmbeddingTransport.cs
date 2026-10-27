namespace Instancium.Vectra.Infrastructure.Transport;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Internal transport abstraction over SignalR HubConnection.
/// Exists solely for unit-testing the adapter without real SignalR connection.
/// </summary>
internal interface IEmbeddingTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<EmbeddingResponse> GenerateEmbeddingsAsync(string repoId, EmbeddingRequest request, CancellationToken cancellationToken = default);

    Task<int> GetEmbeddingDimensionAsync(string repoId, CancellationToken cancellationToken = default);

    Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default);

    Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken cancellationToken = default);
}
