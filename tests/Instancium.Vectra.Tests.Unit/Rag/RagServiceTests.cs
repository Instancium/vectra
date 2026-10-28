namespace Instancium.Vectra.Tests.Unit.Rag;

using System.Runtime.CompilerServices;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Rag;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RagServiceTests
{
    [Fact]
    public async Task RetrieveAsync_ShouldMapTopKSearchResultsToContextChunks()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 7, "target chunk text", new float[] { 1f, 0f });
        store.Upsert(2, 1, 8, "other chunk text", new float[] { 0f, 1f });
        var service = CreateService(store: store);

        RagRetrievalResult result = await service.RetrieveAsync(
            new RagRequest("target chunk text", "repo-test", topK: 1));

        Assert.Equal("target chunk text", result.Question);
        Assert.Single(result.Context);
        Assert.Equal(1, result.Context[0].ChunkId);
        Assert.Equal(7, result.Context[0].PageNumber);
        Assert.Equal("target chunk text", result.Context[0].Snippet);
    }

    [Fact]
    public async Task StreamAnswerAsync_ShouldPassGroundedPromptWithQuestionToChatClient()
    {
        var chat = new StubChatClient(tokens: new[] { "Answer", " token" });
        var service = CreateService(chat: chat);
        var retrieval = new RagRetrievalResult(
            "What is Pt100?",
            new[] { new RagContextChunk(3, 9, 0.7f, "Pt100 nominal resistance.") });

        var streamed = new List<string>();
        await foreach (string token in service.StreamAnswerAsync(retrieval))
        {
            streamed.Add(token);
        }

        Assert.Equal(new[] { "Answer", " token" }, streamed);
        Assert.Single(chat.Prompts);
        Assert.Contains("What is Pt100?", chat.Prompts[0]);
        Assert.Contains("Pt100 nominal resistance.", chat.Prompts[0]);
        Assert.Contains("[1] page 9:", chat.Prompts[0]);
    }

    private static RagService CreateService(
        InMemoryVectorStore? store = null,
        IChatCompletionClient? chat = null) =>
        new(
            new StubInferenceEngine(),
            new InMemoryVectorSearchEngine(store ?? new InMemoryVectorStore()),
            chat ?? new StubChatClient(),
            NullLogger<RagService>.Instance);

    private sealed class StubInferenceEngine : IInferenceEngine
    {
        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
            => Task.FromResult(2);

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string modelId,
            EmbeddingRequest request,
            CancellationToken ct = default)
        {
            float[] vector = request.Text.Contains("target", StringComparison.OrdinalIgnoreCase)
                ? new float[] { 1f, 0f }
                : new float[] { 0f, 1f };
            return Task.FromResult(new EmbeddingResponse(vector, 1, vector.Length));
        }

        public Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ApplyChatTemplateAsync(
            string modelId,
            IEnumerable<ChatMessage> messages,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamDeltaGenerationAsync(
            string sessionId,
            ChatMessage deltaMessage,
            InferenceSettings? overrideSettings = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubChatClient : IChatCompletionClient
    {
        private readonly IReadOnlyList<string> _tokens;
        private readonly List<string> _prompts = new();

        public StubChatClient(IReadOnlyList<string>? tokens = null) =>
            _tokens = tokens ?? new[] { "ok" };

        public IReadOnlyList<string> Prompts
        {
            get
            {
                lock (_prompts)
                {
                    return _prompts.ToArray();
                }
            }
        }

        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (_prompts)
            {
                _prompts.Add(prompt);
            }

            foreach (string token in _tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return token;
            }
        }
    }
}
