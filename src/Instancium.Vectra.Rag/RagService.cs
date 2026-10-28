namespace Instancium.Vectra.Rag;

using System.Runtime.CompilerServices;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Rag.Prompting;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class RagService : IRagService
{
    private readonly IInferenceEngine _embeddingEngine;
    private readonly IVectorSearchEngine _searchEngine;
    private readonly IChatCompletionClient _chatClient;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IInferenceEngine embeddingEngine,
        IVectorSearchEngine searchEngine,
        IChatCompletionClient chatClient,
        ILogger<RagService>? logger = null)
    {
        _embeddingEngine = embeddingEngine ?? throw new ArgumentNullException(nameof(embeddingEngine));
        _searchEngine = searchEngine ?? throw new ArgumentNullException(nameof(searchEngine));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? NullLogger<RagService>.Instance;
    }

    public async Task<RagRetrievalResult> RetrieveAsync(
        RagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        EmbeddingResponse queryVector = await _embeddingEngine.GenerateEmbeddingsAsync(
            request.EmbeddingRepoId,
            new EmbeddingRequest(request.Question, Normalize: true),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SearchResult> matches = await _searchEngine.SearchAsync(
            queryVector.Embedding,
            request.TopK,
            cancellationToken).ConfigureAwait(false);

        List<RagContextChunk> context = matches
            .Select(match => new RagContextChunk(match.ChunkId, match.PageNumber, match.Score, match.Snippet))
            .ToList();

        _logger.LogInformation(
            "RAG retrieval completed. Question={Question}, ContextChunks={Count}",
            request.Question,
            context.Count);

        return new RagRetrievalResult(request.Question, context);
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        RagRetrievalResult retrieval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retrieval);

        string prompt = RagPromptBuilder.Build(retrieval.Question, retrieval.Context);
        await foreach (string token in _chatClient.StreamAnswerAsync(prompt, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }
}
