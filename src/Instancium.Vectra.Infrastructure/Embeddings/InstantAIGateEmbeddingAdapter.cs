// src/Instancium.Vectra.Infrastructure/Embeddings/InstantAIGateEmbeddingAdapter.cs
#nullable enable
namespace Instancium.Vectra.Infrastructure.Embeddings;

using System.Collections.Concurrent;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class InstantAIGateEmbeddingAdapter : IInferenceEngine
{
    private const string QueueFullMarker = "queue is at maximum capacity";
    private const string ContextOverflowMarker = "Context window exceeded";
    private const string DrainingMarker = "currently draining";

    private readonly IEmbeddingTransport _transport;
    private readonly ILogger<InstantAIGateEmbeddingAdapter> _logger;
    private readonly EmbeddingGatewayOptions _options;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _contextSizeByRepo = new();
    private volatile bool _initialized;

    internal InstantAIGateEmbeddingAdapter(
        IEmbeddingTransport transport,
        IOptions<EmbeddingGatewayOptions> options,
        ILogger<InstantAIGateEmbeddingAdapter> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId,
        EmbeddingRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(request);
        await EnsureConnectedAsync(ct);

        int retryCount = 0;
        while (true)
        {
            try
            {
                EmbeddingResponse response = await _transport.GenerateEmbeddingsAsync(modelId, request, ct);
                await ValidateTokenBudgetAsync(modelId, response, ct);
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsQueueFull(ex) && retryCount < _options.MaxRetryAttempts)
            {
                retryCount++;
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                _logger.LogWarning(
                    "Embedding queue full. Retry {RetryCount}/{MaxRetries} after {DelayMs} ms.",
                    retryCount,
                    _options.MaxRetryAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex) when (IsQueueFull(ex))
            {
                throw new VectraException(
                    $"Embedding queue remained full after {retryCount} retries for model '{modelId}'. Details: {ex.Message}",
                    ex);
            }
            catch (Exception ex) when (Matches(ex, ContextOverflowMarker))
            {
                throw new VectraException(
                    $"Context overflow while embedding for model '{modelId}'. " +
                    "Reduce chunk size (Vectra:Indexing:ChunkSizeChars) or raise profile ContextSize.",
                    ex);
            }
            catch (Exception ex) when (Matches(ex, DrainingMarker))
            {
                throw new VectraException(
                    $"Gateway is draining (model swap in progress) for model '{modelId}'. Retry later.",
                    ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embeddings for model {ModelId}.", modelId);
                throw new VectraException($"Embedding generation failed: {ex.Message}", ex);
            }
        }
    }

    public async Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        await EnsureConnectedAsync(ct);
        try
        {
            return await _transport.GetEmbeddingDimensionAsync(modelId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new VectraException($"Failed to get embedding dimension: {ex.Message}", ex);
        }
    }

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

    private async Task ValidateTokenBudgetAsync(string modelId, EmbeddingResponse response, CancellationToken ct)
    {
        int contextSize = await GetContextSizeAsync(modelId, ct);
        if (contextSize > 0 && response.TokenCount > contextSize)
        {
            throw new VectraException(
                $"Embedding TokenCount {response.TokenCount} exceeds model ContextSize {contextSize} for '{modelId}'.");
        }
    }

    private async Task<int> GetContextSizeAsync(string modelId, CancellationToken ct)
    {
        if (_contextSizeByRepo.TryGetValue(modelId, out int cached))
        {
            return cached;
        }

        var details = await _transport.GetActiveModelDetailsAsync(ct);
        int contextSize = details.ContextSize;
        _contextSizeByRepo[modelId] = contextSize;
        return contextSize;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _transport.ConnectAsync(ct);
            _initialized = true;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    internal static bool IsQueueFull(Exception ex) =>
        Matches(ex, QueueFullMarker) || HasTypeName(ex, "QueueFull");

    private static bool Matches(Exception ex, string marker)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTypeName(Exception ex, string typeNameFragment)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name.Contains(typeNameFragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}