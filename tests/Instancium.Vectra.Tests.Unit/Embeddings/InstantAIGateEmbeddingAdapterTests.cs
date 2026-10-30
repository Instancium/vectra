// tests/Instancium.Vectra.Tests.Unit/Embeddings/InstantAIGateEmbeddingAdapterTests.cs
#nullable enable
namespace Instancium.Vectra.Tests.Unit.Embeddings;

using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using Xunit;

[Trait("Category", "Unit")]
public class InstantAIGateEmbeddingAdapterTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldReturnCanonicalResponse()
    {
        var expectedResponse = new EmbeddingResponse(new float[] { 0.1f, 0.2f, 0.3f }, 10, 3);
        var transport = new StubEmbeddingTransport(response: expectedResponse);
        var adapter = CreateAdapter(transport);

        var result = await adapter.GenerateEmbeddingsAsync("test-model", new EmbeddingRequest("test text"));

        Assert.Equal(expectedResponse, result);
        Assert.Equal(3, result.EmbeddingDimension);
        Assert.Equal(1, transport.GenerateCalls);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldRetryOnCanonicalQueueFullMessageAndSucceed()
    {
        var transport = new StubEmbeddingTransport(
            response: new EmbeddingResponse(new float[] { 1f }, 1, 1),
            throwSequence: new Queue<Exception>(new Exception[]
            {
                new HubException(new QueueFullException(4).Message)
            }));
        var adapter = CreateAdapter(transport, maxRetryAttempts: 3);

        var result = await adapter.GenerateEmbeddingsAsync("test-model", new EmbeddingRequest("test"));

        Assert.Equal(2, transport.GenerateCalls);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldMapQueueFullToVectraExceptionAfterRetries()
    {
        var transport = new StubEmbeddingTransport(
            throwSequence: new Queue<Exception>(new Exception[]
            {
                new HubException(new QueueFullException(4).Message),
                new HubException(new QueueFullException(4).Message),
                new HubException(new QueueFullException(4).Message)
            }));
        var adapter = CreateAdapter(transport, maxRetryAttempts: 2);

        var ex = await Assert.ThrowsAsync<VectraException>(() =>
            adapter.GenerateEmbeddingsAsync("test-model", new EmbeddingRequest("test")));

        Assert.Equal(3, transport.GenerateCalls);
        Assert.Contains("maximum capacity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldMapContextOverflowWithoutRetry()
    {
        var transport = new StubEmbeddingTransport(
            throwSequence: new Queue<Exception>(new Exception[]
            {
                new HubException(new ContextOverflowException("s", 2000, 500, 64, 2048).Message)
            }));
        var adapter = CreateAdapter(transport, maxRetryAttempts: 3);

        var ex = await Assert.ThrowsAsync<VectraException>(() =>
            adapter.GenerateEmbeddingsAsync("test-model", new EmbeddingRequest("test")));

        Assert.Equal(1, transport.GenerateCalls);
        Assert.Contains("Context overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldRejectTokenCountAboveContextSize()
    {
        var transport = new StubEmbeddingTransport(
            response: new EmbeddingResponse(new float[] { 1f }, 5000, 1),
            contextSize: 4096);
        var adapter = CreateAdapter(transport);

        var ex = await Assert.ThrowsAsync<VectraException>(() =>
            adapter.GenerateEmbeddingsAsync("test-model", new EmbeddingRequest("test")));

        Assert.Contains("ContextSize", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldPropagateCancellation()
    {
        var transport = new StubEmbeddingTransport();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var adapter = CreateAdapter(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.GenerateEmbeddingsAsync("test-model", new EmbeddingRequest("test"), cts.Token));
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_ShouldReturnDimension()
    {
        var transport = new StubEmbeddingTransport(dimension: 1024);
        var adapter = CreateAdapter(transport);

        Assert.Equal(1024, await adapter.GetEmbeddingDimensionAsync("test-model"));
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_ShouldWrapErrorInVectraException()
    {
        var transport = new StubEmbeddingTransport(throwOnDimension: new InvalidOperationException("not ready"));
        var adapter = CreateAdapter(transport);

        await Assert.ThrowsAsync<VectraException>(() => adapter.GetEmbeddingDimensionAsync("test-model"));
    }

    [Fact]
    public async Task UnsupportedInferenceEngineMembers_ShouldThrowNotSupportedException()
    {
        var adapter = CreateAdapter(new StubEmbeddingTransport());

        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.TokenizeDataAsync("m", "t"));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.ApplyChatTemplateAsync("m", new[] { new ChatMessage("user", "hi") }));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.StreamDeltaGenerationAsync("s", new ChatMessage("user", "hi")).GetAsyncEnumerator(CancellationToken.None).MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldNotReconnectOnSubsequentCalls()
    {
        var transport = new StubEmbeddingTransport(response: new EmbeddingResponse(new float[] { 1f }, 1, 1));
        var adapter = CreateAdapter(transport);

        await adapter.GenerateEmbeddingsAsync("m", new EmbeddingRequest("a"));
        await adapter.GenerateEmbeddingsAsync("m", new EmbeddingRequest("b"));

        Assert.Equal(1, transport.ConnectCalls);
        Assert.Equal(2, transport.GenerateCalls);
    }

    private static InstantAIGateEmbeddingAdapter CreateAdapter(
        StubEmbeddingTransport transport,
        int maxRetryAttempts = 3) =>
        new(
            transport,
            Options.Create(new EmbeddingGatewayOptions { MaxRetryAttempts = maxRetryAttempts }),
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

    private sealed class StubEmbeddingTransport : IEmbeddingTransport
    {
        private readonly EmbeddingResponse? _response;
        private readonly Queue<Exception> _throwSequence;
        private readonly int _dimension;
        private readonly int _contextSize;
        private readonly Exception? _throwOnDimension;
        private int _generateCalls;
        private int _connectCalls;

        public StubEmbeddingTransport(
            EmbeddingResponse? response = null,
            Queue<Exception>? throwSequence = null,
            int dimension = 1024,
            int contextSize = 4096,
            Exception? throwOnDimension = null)
        {
            _response = response;
            _throwSequence = throwSequence ?? new Queue<Exception>();
            _dimension = dimension;
            _contextSize = contextSize;
            _throwOnDimension = throwOnDimension;
        }

        public int GenerateCalls => Volatile.Read(ref _generateCalls);
        public int ConnectCalls => Volatile.Read(ref _connectCalls);
        public string? LastRepoId { get; private set; }
        public EmbeddingRequest? LastRequest { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectCalls);
            return Task.CompletedTask;
        }

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string repoId,
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _generateCalls);
            LastRepoId = repoId;
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            if (_throwSequence.Count > 0)
            {
                return Task.FromException<EmbeddingResponse>(_throwSequence.Dequeue());
            }

            return Task.FromResult(_response ?? new EmbeddingResponse(new float[] { 0f }, 1, 1));
        }

        public Task<int> GetEmbeddingDimensionAsync(string repoId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_throwOnDimension is not null)
            {
                return Task.FromException<int>(_throwOnDimension);
            }

            return Task.FromResult(_dimension);
        }

        public Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready, ActiveModelId = LastRepoId });

        public Task<NativeModelDetails> GetActiveModelDetailsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NativeModelDetails
            {
                RepoId = LastRepoId ?? string.Empty,
                ContextSize = _contextSize,
                EmbeddingDimension = _dimension,
                IsEmbedding = true
            });

        public Task<IReadOnlyList<NativeModelDetails>> GetLoadedModelsDetailsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = new[]
            {
                new NativeModelDetails
                {
                    RepoId = LastRepoId ?? string.Empty,
                    ContextSize = _contextSize,
                    EmbeddingDimension = _dimension,
                    IsEmbedding = true
                }
            };
            return Task.FromResult<IReadOnlyList<NativeModelDetails>>(details);
        }

        public ValueTask DisposeAsync() => default;
    }
}