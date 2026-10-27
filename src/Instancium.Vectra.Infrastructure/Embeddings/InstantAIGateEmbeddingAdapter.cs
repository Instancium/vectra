namespace Instancium.Vectra.Infrastructure.Embeddings;

using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

public sealed class InstantAIGateEmbeddingAdapter : IInferenceEngine
{
    private readonly IEmbeddingTransport _transport;
    private readonly ILogger<InstantAIGateEmbeddingAdapter> _logger;
    private readonly EmbeddingGatewayOptions _options;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _initialized;

    internal InstantAIGateEmbeddingAdapter(
        IEmbeddingTransport transport,
        IOptions<EmbeddingGatewayOptions> options,
        ILogger<InstantAIGateEmbeddingAdapter> logger)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId,
        EmbeddingRequest request,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        int retryCount = 0;
        while (true)
        {
            try
            {
                return await _transport.GenerateEmbeddingsAsync(modelId, request, ct);
            }
            catch (Exception ex) when (IsQueueFullException(ex) && retryCount < _options.MaxRetryAttempts)
            {
                retryCount++;
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                _logger.LogWarning(ex, "Queue full (429). Retry {RetryCount}/{MaxRetries} after {Delay}ms",
                    retryCount, _options.MaxRetryAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embeddings for model {ModelId}", modelId);
                throw new VectraException($"Embedding generation failed: {ex.Message}", ex);
            }
        }
    }

    public async Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        try
        {
            return await _transport.GetEmbeddingDimensionAsync(modelId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new VectraException($"Failed to get embedding dimension: {ex.Message}", ex);
        }
    }

    // Other IInferenceEngine methods not needed for embeddings MVP
    public Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
        => throw new NotSupportedException("Tokenization is not supported in embedding-only adapter.");

    public Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
        => throw new NotSupportedException("Chat template is not supported in embedding-only adapter.");

    public IAsyncEnumerable<string> StreamDeltaGenerationAsync(
        string sessionId,
        ChatMessage deltaMessage,
        InferenceSettings? overrideSettings = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("Streaming is not supported in embedding-only adapter.");

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await _transport.ConnectAsync(ct);
            _initialized = true;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static bool IsQueueFullException(Exception ex)
    {
        return ex.Message.Contains("429") ||
               ex.GetType().Name.Contains("QueueFull") ||
               (ex.InnerException != null && IsQueueFullException(ex.InnerException));
    }
}
