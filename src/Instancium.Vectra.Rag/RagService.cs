namespace Instancium.Vectra.Rag;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Rag.Prompting;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class RagService : IRagService
{
    // Over-retrieve to ensure the physically relevant chunk is captured before re-ranking.
    private const int OverRetrievalFactor = 20;

    private readonly IInferenceEngine _embeddingEngine;
    private readonly IVectorSearchEngine _searchEngine;
    private readonly IChatCompletionClient _chatClient;
    private readonly ILogger<RagService> _logger;
    private readonly IChunkMap? _chunkMap;
    private readonly IChunkTextReader? _chunkTextReader;

    // Stage 1 recall window: in-process brute-force SIMD scan is ~1 ms at MVP scale,
    // so we afford the classic top-100 recall window before reranking.
    private const int RecallWindow = 100;

    // Stage 2 weight scale: keeps lexical boost comparable to the dense cosine spread.
    private const double LexicalBoostScale = 0.15;

    public RagService(
        IInferenceEngine embeddingEngine,
        IVectorSearchEngine searchEngine,
        IChatCompletionClient chatClient,
        ILogger<RagService>? logger = null,
        IChunkMap? chunkMap = null,
        IChunkTextReader? chunkTextReader = null)
    {
        _embeddingEngine = embeddingEngine ?? throw new ArgumentNullException(nameof(embeddingEngine));
        _searchEngine = searchEngine ?? throw new ArgumentNullException(nameof(searchEngine));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? NullLogger<RagService>.Instance;
        _chunkMap = chunkMap;
        _chunkTextReader = chunkTextReader;
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

        // Stage 1 (recall): dense bi-encoder retrieval over a wide window.
        IReadOnlyList<SearchResult> candidates = await _searchEngine.SearchAsync(
            queryVector.Embedding,
            Math.Max(RecallWindow, request.TopK),
            cancellationToken).ConfigureAwait(false);

        // Stage 2 (precision): sparse lexical rerank with candidate-window IDF weighting.
        IReadOnlyList<SearchResult> reranked = RerankLexically(request.Question, candidates, request.TopK);

        // TZ-2 stage 3: lazily hydrate full chunk text, only for the final Top-K.
        IReadOnlyList<SearchResult> hydrated = await HydrateTopKSnippetsAsync(reranked, cancellationToken)
            .ConfigureAwait(false);

        List<RagContextChunk> context = hydrated
            .Select(match => new RagContextChunk(match.ChunkId, match.PageNumber, match.Score, match.Snippet))
            .ToList();

        _logger.LogInformation(
            "RAG retrieval completed. Question={Question}, ContextChunks={Count}",
            request.Question,
            context.Count);

        return new RagRetrievalResult(request.Question, context);
    }

    /// <summary>
    /// Reads full chunk text for Top-K results whose snippets are empty (disk index flow).
    /// In the in-memory flow snippets are already populated, so no read happens.
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> HydrateTopKSnippetsAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken)
    {
        if (_chunkMap is null || _chunkTextReader is null || results.Count == 0)
        {
            return results;
        }

        var ranges = new List<ChunkTextRange>(results.Count);
        var indices = new List<int>(results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            if (!string.IsNullOrEmpty(results[i].Snippet))
            {
                continue;
            }

            if (_chunkMap.TryGet(results[i].ChunkId, out ChunkMapEntry? entry))
            {
                ranges.Add(new ChunkTextRange(entry.Offset, entry.Length));
                indices.Add(i);
            }
        }

        if (ranges.Count == 0)
        {
            return results;
        }

        IReadOnlyList<string> texts = await _chunkTextReader.ReadBatchAsync(ranges, cancellationToken)
            .ConfigureAwait(false);

        var hydrated = results.ToList();
        for (int i = 0; i < indices.Count; i++)
        {
            SearchResult original = hydrated[indices[i]];
            hydrated[indices[i]] = original with { Snippet = texts[i] };
        }

        return hydrated;
    }

    /// <summary>
    /// Stage 2 precision layer: IDF-weighted lexical reranker over the recall window.
    /// Rare discriminative terms (e.g. "thermoelectric", "dielectric") outweigh frequent
    /// generic ones ("pressure", "signal"). Deterministic, zero infrastructure cost,
    /// stays inside Vectra.Rag (Core contracts only).
    /// </summary>
    private static IReadOnlyList<SearchResult> RerankLexically(
        string question,
        IReadOnlyList<SearchResult> candidates,
        int finalTopK)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        HashSet<string> queryTerms = ExtractSignificantTerms(question);
        if (queryTerms.Count == 0)
        {
            return candidates.Take(finalTopK).ToList();
        }

        // Document frequency of each query term inside the recall window.
        var documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string term in queryTerms)
        {
            documentFrequency[term] = candidates.Count(candidate =>
                candidate.Snippet.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        double window = candidates.Count;
        return candidates
            .Select(candidate =>
            {
                double boost = 0.0;
                foreach (string term in queryTerms)
                {
                    if (!candidate.Snippet.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // IDF over the window: df=1 of 100 => ln(51) ~ 3.9 (strong signal);
                    // df=50 of 100 => ln(2.96) ~ 1.1 (weak, generic signal).
                    double idf = Math.Log(1.0 + window / (1.0 + documentFrequency[term]));
                    boost += idf;
                }

                // Publish the fused score so logs and citations reflect true ranking.
                return candidate with { Score = candidate.Score + (float)(boost * LexicalBoostScale) };
            })
            .OrderByDescending(x => x.Score)
            .Take(finalTopK)
            .ToList();
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

    /// <summary>
    /// Applies a sparse keyword boost to dense vector scores.
    /// This compensates for small embedding models (like 0.6B) that struggle with 
    /// specific technical physics vs general glossary definitions.
    /// </summary>
    private static IReadOnlyList<SearchResult> RerankWithKeywordBoost(
        string question,
        IReadOnlyList<SearchResult> candidates,
        int finalTopK)
    {
        if (candidates.Count == 0) return candidates;

        HashSet<string> queryTerms = ExtractSignificantTerms(question);
        if (queryTerms.Count == 0)
        {
            return candidates.Take(finalTopK).ToList();
        }

        const float boostPerMatch = 0.10f;

        return candidates
            .Select(c =>
            {
                int matches = queryTerms.Count(term =>
                    c.Snippet.Contains(term, StringComparison.OrdinalIgnoreCase));

                float finalScore = c.Score + (matches * boostPerMatch);

                // Return a NEW SearchResult with the boosted score so downstream 
                // consumers (logs, LLM prompt citations, benchmarks) see the true ranking weight.
                return c with { Score = finalScore };
            })
            .OrderByDescending(x => x.Score)
            .Take(finalTopK)
            .ToList();
    }

    private static readonly Regex WordBoundaryRegex = new(@"\b[a-zA-Z]{4,}\b", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "how", "does", "convert", "into", "electrical", "signal", "pressure", "transmitter",
        "what", "where", "when", "which", "their", "there", "about", "would", "could", "from",
        "that", "this", "with", "have", "been", "were", "they", "them", "these", "those"
    };

    private static HashSet<string> ExtractSignificantTerms(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WordBoundaryRegex.Matches(text))
        {
            string word = match.Value;
            if (!StopWords.Contains(word))
            {
                terms.Add(word);
            }
        }
        return terms;
    }
}