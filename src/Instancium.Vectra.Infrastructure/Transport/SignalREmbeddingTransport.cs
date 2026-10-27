namespace Instancium.Vectra.Infrastructure.Transport;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using Instancium.Vectra.Infrastructure.Configuration;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class SignalREmbeddingTransport : IEmbeddingTransport
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalREmbeddingTransport> _logger;
    private bool _disposed;

    public SignalREmbeddingTransport(
        IOptions<EmbeddingGatewayOptions> options,
        ILogger<SignalREmbeddingTransport> logger)
    {
        _logger = logger;
        var opts = options.Value;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{opts.Url}{opts.HubPath}", httpOptions =>
            {
                httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(opts.ApiKey);
            })
            .WithAutomaticReconnect()
            .Build();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            return;
        }

        await _connection.StartAsync(cancellationToken);
    }

    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string repoId,
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _connection.InvokeAsync<EmbeddingResponse>(
            "GenerateEmbeddingsAsync",
            repoId,
            request,
            cancellationToken);
    }

    public async Task<int> GetEmbeddingDimensionAsync(
        string repoId,
        CancellationToken cancellationToken = default)
    {
        return await _connection.InvokeAsync<int>(
            "GetEmbeddingDimensionAsync",
            repoId,
            cancellationToken);
    }

    public async Task<GatewayStatusDetails> GetGatewayStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return await _connection.InvokeAsync<GatewayStatusDetails>(
            "GetGatewayStatus",
            cancellationToken);
    }

    public async Task<NativeModelDetails> GetActiveModelDetailsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _connection.InvokeAsync<NativeModelDetails>(
            "GetActiveModelDetailsAsync",
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _connection.DisposeAsync();
    }
}
