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
public sealed class RagServiceLazyTextTests
{
    [Fact]
    public async Task RetrieveAsync_ShouldHydrateEmptySnippets_OnlyForTopK()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 7, string.Empty, new float[] { 1f, 0f });
        store.Upsert(2, 1, 8, string.Empty, new float[] { 0.9f, 0.1f });
        store.Upsert(3, 1, 9, string.Empty, new float[] { 0f, 1f });

        var chunkMap = new StubChunkMap(
            new ChunkMapEntry(1, 1, 7, 0, 10, true),
            new ChunkMapEntry(2, 1, 8, 10, 10, true),
            new ChunkMapEntry(3, 1, 9, 20, 10, true));
        var textReader = new StubChunkTextReader(
            (0, "text for chunk one"),
            (10, "text for chunk two"),
            (20, "text for chunk three"));

        var service = new RagService(
            new StubInferenceEngine(),
            new InMemoryVectorSearchEngine(store),
            new StubChatClient(),
            NullLogger<RagService>.Instance,
            chunkMap,
            textReader);

        RagRetrievalResult result = await service.RetrieveAsync(
            new RagRequest("target", "repo-test", topK: 1));

        Assert.Single(result.Context);
        Assert.Equal(1, result.Context[0].ChunkId);
        Assert.Equal("text for chunk one", result.Context[0].Snippet);

        Assert.Equal(1, textReader.ReadBatchCallCount);
        Assert.Equal(1, textReader.TotalRangesRead);
    }

    private sealed class StubInferenceEngine : IInferenceEngine
    {
        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
            => Task.FromResult(2);

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string modelId,
            EmbeddingRequest request,
            CancellationToken ct = default)
        {
            float[] vector = new float[] { 1f, 0f };
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
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return "ok";
        }
    }

    private sealed class StubChunkMap : IChunkMap
    {
        private readonly Dictionary<long, ChunkMapEntry> _entries;

        public StubChunkMap(params ChunkMapEntry[] entries) =>
            _entries = entries.ToDictionary(entry => entry.ChunkId);

        public int Count => _entries.Count;

        public IReadOnlyList<ChunkMapEntry> Entries =>
            _entries.Values.OrderBy(entry => entry.ChunkId).ToList();

        public ChunkMapEntry Get(long chunkId) => _entries[chunkId];

        public bool TryGet(long chunkId, out ChunkMapEntry? entry) =>
            _entries.TryGetValue(chunkId, out entry);

        public IReadOnlyList<long> GetChunkIdsByFile(long fileId) =>
            _entries.Values
                .Where(entry => entry.FileId == fileId)
                .Select(entry => entry.ChunkId)
                .OrderBy(id => id)
                .ToList();
    }

    private sealed class StubChunkTextReader : IChunkTextReader
    {
        private readonly Dictionary<long, string> _textByOffset;

        public StubChunkTextReader(params (long Offset, string Text)[] texts) =>
            _textByOffset = texts.ToDictionary(t => t.Offset, t => t.Text);

        public int ReadBatchCallCount { get; private set; }
        public int TotalRangesRead { get; private set; }

        public ValueTask<string> ReadAsync(ChunkTextRange range, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_textByOffset[range.Offset]);

        public Task<IReadOnlyList<string>> ReadBatchAsync(
            IReadOnlyList<ChunkTextRange> ranges,
            CancellationToken cancellationToken = default)
        {
            ReadBatchCallCount++;
            TotalRangesRead += ranges.Count;
            IReadOnlyList<string> texts = ranges.Select(range => _textByOffset[range.Offset]).ToList();
            return Task.FromResult(texts);
        }
    }
}
