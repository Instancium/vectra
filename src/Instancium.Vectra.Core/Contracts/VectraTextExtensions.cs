#nullable enable
namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// High-level text extension methods over <see cref="IVectraCollection"/> and <see cref="IEmbeddingProvider"/>.
/// Decouples raw text ingestion and semantic retrieval from specific machine learning frameworks,
/// preserving zero-dependency architecture for the storage and similarity kernels.
/// </summary>
public static class VectraTextExtensions
{
    /// <summary>
    /// Computes the vector embedding of <paramref name="text"/> using <paramref name="provider"/> and adds it to the collection.
    /// Falls back to storing <paramref name="text"/> as the payload when <paramref name="payload"/> is omitted,
    /// enabling single-expression document ingestion.
    /// </summary>
    /// <param name="collection">Target vector collection.</param>
    /// <param name="provider">Embedding provider generating the representation vector.</param>
    /// <param name="text">Source text to be converted to vector representation.</param>
    /// <param name="payload">Optional payload override. Defaults to <paramref name="text"/> if <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token to abort asynchronous processing.</param>
    /// <returns>Assigned unique vector identifier.</returns>
    public static async ValueTask<long> AddTextAsync(
        this IVectraCollection collection,
        IEmbeddingProvider provider,
        string text,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        ReadOnlyMemory<float> vector = await provider.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
        var entry = new VectorEntry(vectorId: null, vector: vector, payload: payload ?? text);
        return await collection.AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the vector embedding of <paramref name="query"/> and performs vector search across <paramref name="collection"/>.
    /// Explicitly passes <paramref name="query"/> as <see cref="SearchQuery.QueryText"/>, serving as the sole official conduit
    /// for downstream lexical, BM25, and neural cross-encoder <see cref="IReranker"/> implementations to access query tokens.
    /// </summary>
    /// <param name="collection">Target vector collection.</param>
    /// <param name="provider">Embedding provider used to embed the query.</param>
    /// <param name="query">Raw query string.</param>
    /// <param name="options">Optional execution parameters controlling Top-K, overfetch, and reranking.</param>
    /// <param name="cancellationToken">Cancellation token to abort asynchronous search.</param>
    /// <returns>Ranked search results with populated scores and lazy-read payloads.</returns>
    public static async Task<IReadOnlyList<VectorSearchHit>> SearchTextAsync(
        this IVectraCollection collection,
        IEmbeddingProvider provider,
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        ReadOnlyMemory<float> vector = await provider.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        var searchQuery = new SearchQuery(vector, queryText: query);
        return await collection.SearchAsync(searchQuery, options, cancellationToken).ConfigureAwait(false);
    }
}
