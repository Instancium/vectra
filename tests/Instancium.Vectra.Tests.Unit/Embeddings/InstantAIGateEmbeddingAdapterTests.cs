#nullable enable

namespace Instancium.Vectra.Tests.Unit.Embeddings;

using InstantAIGate.Core.Dtos.Inference;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Infrastructure.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public class InstantAIGateEmbeddingAdapterTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldReturnCanonicalResponse()
    {
        // Arrange
        var expectedResponse = new EmbeddingResponse(new float[] { 0.1f, 0.2f, 0.3f }, 10, 3);
        var transport = new StubEmbeddingTransport(expectedResponse);
        var options = Options.Create(new EmbeddingGatewayOptions { MaxRetryAttempts = 3 });
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act
        var result = await adapter.GenerateEmbeddingsAsync(
            "test-model",
            new EmbeddingRequest("test text"));

        // Assert
        Assert.Equal(expectedResponse, result);
        Assert.Equal(3, result.EmbeddingDimension);
        Assert.Equal(1, transport.GenerateCalls);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldMapQueueFullToVectraExceptionWithRetry()
    {
        // Arrange
        var transport = new StubEmbeddingTransport(
            throwOnGenerate: new Exception("429 Queue full"));
        var options = Options.Create(new EmbeddingGatewayOptions { MaxRetryAttempts = 2 });
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<VectraException>(() =>
            adapter.GenerateEmbeddingsAsync(
                "test-model",
                new EmbeddingRequest("test")));

        // 1 initial + 2 retries = 3 calls
        Assert.Equal(3, transport.GenerateCalls);
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldPropagateCancellation()
    {
        // Arrange
        var transport = new StubEmbeddingTransport();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = Options.Create(new EmbeddingGatewayOptions());
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.GenerateEmbeddingsAsync(
                "test-model",
                new EmbeddingRequest("test"),
                cts.Token));
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_ShouldReturnDimension()
    {
        // Arrange
        var transport = new StubEmbeddingTransport(dimension: 1024);
        var options = Options.Create(new EmbeddingGatewayOptions());
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act
        var dimension = await adapter.GetEmbeddingDimensionAsync("test-model");

        // Assert
        Assert.Equal(1024, dimension);
    }

    [Fact]
    public async Task GetEmbeddingDimensionAsync_ShouldWrapErrorInVectraException()
    {
        // Arrange
        var transport = new StubEmbeddingTransport(
            throwOnDimension: new InvalidOperationException("not ready"));
        var options = Options.Create(new EmbeddingGatewayOptions());
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<VectraException>(() =>
            adapter.GetEmbeddingDimensionAsync("test-model"));
    }

    /// <summary>
    /// Minimal hand-written stub for IEmbeddingTransport.
    /// No external mocking libraries.
    /// </summary>
    private sealed class StubEmbeddingTransport : IEmbeddingTransport
    {

        private readonly EmbeddingResponse? _response;
        private readonly Exception? _throwOnGenerate;
        private readonly int _dimension;
        private readonly Exception? _throwOnDimension;

        public int GenerateCalls { get; private set; }
        public int DimensionCalls { get; private set; }

        public int ConnectCalls { get; private set; }
        public string? LastRepoId { get; private set; }
        public EmbeddingRequest? LastRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public StubEmbeddingTransport(
            EmbeddingResponse? response = null,
            Exception? throwOnGenerate = null,
            int dimension = 0,
            Exception? throwOnDimension = null)
        {
            _response = response;
            _throwOnGenerate = throwOnGenerate;
            _dimension = dimension;
            _throwOnDimension = throwOnDimension;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            return Task.CompletedTask;
        }

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string repoId,
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            LastRepoId = repoId;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();

            if (_throwOnGenerate != null)
                return Task.FromException<EmbeddingResponse>(_throwOnGenerate);

            return Task.FromResult(_response
                ?? new EmbeddingResponse(Array.Empty<float>(), 0, 0));
        }

        public Task<int> GetEmbeddingDimensionAsync(
            string repoId,
            CancellationToken cancellationToken = default)
        {
            DimensionCalls++;
            cancellationToken.ThrowIfCancellationRequested();

            if (_throwOnDimension != null)
                return Task.FromException<int>(_throwOnDimension);

            return Task.FromResult(_dimension);
        }

        public Task<InstantAIGate.Core.Dtos.Status.GatewayStatusDetails> GetGatewayStatusAsync(
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<InstantAIGate.Core.Dtos.Status.NativeModelDetails> GetActiveModelDetailsAsync(
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask DisposeAsync() => default;
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldDelegateToTransportWithCorrectArguments()
    {
        // Arrange
        var expectedResponse = new EmbeddingResponse(
            new float[] { 0.1f, 0.2f, 0.3f }, 10, 3);
        var transport = new StubEmbeddingTransport(expectedResponse);
        var options = Options.Create(new EmbeddingGatewayOptions { MaxRetryAttempts = 3 });
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport,
            options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        var inputModelId = "qwen3-embedding-0.6b";
        var inputRequest = new EmbeddingRequest("Hello world", Normalize: true);

        // Act
        var result = await adapter.GenerateEmbeddingsAsync(
            inputModelId, inputRequest);

        // Assert — поведенческие инварианты
        Assert.Equal(1, transport.GenerateCalls);
        Assert.Equal(inputModelId, transport.LastRepoId);          
        Assert.Equal("Hello world", transport.LastRequest?.Text);    
        Assert.True(transport.LastRequest?.Normalize);              
        Assert.Equal(expectedResponse, result);
        Assert.Equal(1, transport.ConnectCalls);                   
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldNotReconnectOnSubsequentCalls()
    {
        // Arrange
        var transport = new StubEmbeddingTransport(
            new EmbeddingResponse(Array.Empty<float>(), 0, 0));
        var options = Options.Create(new EmbeddingGatewayOptions());
        var adapter = new InstantAIGateEmbeddingAdapter(
            transport, options,
            NullLogger<InstantAIGateEmbeddingAdapter>.Instance);

        // Act — два вызова подряд
        await adapter.GenerateEmbeddingsAsync("m", new EmbeddingRequest("a"));
        await adapter.GenerateEmbeddingsAsync("m", new EmbeddingRequest("b"));

        // Assert — Connect вызван только один раз
        Assert.Equal(1, transport.ConnectCalls);
        Assert.Equal(2, transport.GenerateCalls);
    }
}