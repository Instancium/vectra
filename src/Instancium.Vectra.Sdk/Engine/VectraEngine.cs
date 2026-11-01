namespace Instancium.Vectra.Sdk.Engine;

using System.Collections.Concurrent;
using System.Diagnostics;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Sdk.Configuration;
using Instancium.Vectra.Sdk.Contracts;
using Instancium.Vectra.Sdk.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

public sealed class VectraEngine : IVectraEngine
{
    private readonly IIncrementalIndexer _indexer;
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IIndexReader _indexReader;
    private readonly IInstanciumDirectoryResolver _directoryResolver;
    private readonly VectraSdkOptions _sdkOptions;
    private readonly EmbeddingGatewayOptions _embeddingOptions;
    private readonly ILogger<VectraEngine> _logger;

    private readonly ConcurrentDictionary<string, ProfileSessionHolder> _activeSessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    public VectraEngine(
        IIncrementalIndexer indexer,
        IInferenceEngine inferenceEngine,
        IIndexReader indexReader,
        IInstanciumDirectoryResolver directoryResolver,
        IOptions<VectraSdkOptions> sdkOptions,
        IOptions<EmbeddingGatewayOptions> embeddingOptions,
        ILogger<VectraEngine>? logger = null)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _inferenceEngine = inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine));
        _indexReader = indexReader ?? throw new ArgumentNullException(nameof(indexReader));
        _directoryResolver = directoryResolver ?? throw new ArgumentNullException(nameof(directoryResolver));
        _sdkOptions = sdkOptions?.Value ?? new VectraSdkOptions();
        _embeddingOptions = embeddingOptions?.Value ?? new EmbeddingGatewayOptions();
        _logger = logger ?? NullLogger<VectraEngine>.Instance;
    }

    public async Task<IndexResult> IndexFolderAsync(
        string profileId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        string indexDirectory = ResolveIndexDirectory(profileId);
        _directoryResolver.EnsureDirectoryStructure(indexDirectory);

        string repoId = !string.IsNullOrWhiteSpace(_embeddingOptions.RepoId)
            ? _embeddingOptions.RepoId
            : _sdkOptions.DefaultEmbeddingRepoId;

        _logger.LogInformation(
            "Starting index run. Profile={ProfileId}, ContentRoot={ContentRoot}, IndexDir={IndexDir}, Repo={RepoId}",
            profileId, folderPath, indexDirectory, repoId);

        IncrementalIndexingResult result = await _indexer.RunAsync(
            folderPath,
            indexDirectory,
            repoId,
            cancellationToken).ConfigureAwait(false);

        // Invalidate cached in-memory session to ensure next search reloads refreshed indices
        _activeSessions.TryRemove(profileId, out _);

        stopwatch.Stop();

        _logger.LogInformation(
            "Indexing completed for Profile={ProfileId}. Added={Added}, Deactivated={Deactivated}, HasChanges={HasChanges}, Elapsed={Elapsed}ms",
            profileId,
            result.ChunksAdded,
            result.ChunksDeactivated,
            result.Changes.HasChanges,
            stopwatch.ElapsedMilliseconds);

        return new IndexResult(
            profileId: profileId,
            folderPath: folderPath,
            indexDirectory: indexDirectory,
            filesIndexed: result.Manifest.FileCount,
            chunksAdded: result.ChunksAdded,
            chunksDeactivated: result.ChunksDeactivated,
            hasChanges: result.Changes.HasChanges,
            duration: stopwatch.Elapsed,
            manifest: result.Manifest);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string profileId,
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        cancellationToken.ThrowIfCancellationRequested();

        string indexDirectory = ResolveIndexDirectory(profileId);
        string manifestPath = Path.Combine(indexDirectory, IndexArtifactNames.Manifest);

        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("Search skipped: index manifest not found at '{Path}'.", manifestPath);
            return Array.Empty<SearchResult>();
        }

        ProfileSessionHolder sessionHolder = await GetOrOpenSessionAsync(profileId, indexDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (sessionHolder.Session.ChunkMap.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        string repoId = sessionHolder.Session.Manifest.ModelId;

        EmbeddingResponse queryEmbedding = await _inferenceEngine.GenerateEmbeddingsAsync(
            repoId,
            new EmbeddingRequest(query, Normalize: true),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SearchResult> rawResults = await sessionHolder.SearchEngine.SearchAsync(
            queryEmbedding.Embedding,
            topK,
            cancellationToken).ConfigureAwait(false);

        if (rawResults.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        // Snippet Hydration: Read actual chunk text from chunks.bin via session
        var hydratedResults = new List<SearchResult>(rawResults.Count);
        foreach (SearchResult match in rawResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string snippet = match.Snippet;
            if (string.IsNullOrWhiteSpace(snippet))
            {
                try
                {
                    snippet = await sessionHolder.Session.GetChunkTextAsync(match.ChunkId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to hydrate chunk text for ChunkId={ChunkId}", match.ChunkId);
                    snippet = string.Empty;
                }
            }

            hydratedResults.Add(new SearchResult(
                match.ChunkId,
                match.FileId,
                match.PageNumber,
                match.Score,
                snippet,
                match.Vector));
        }

        return hydratedResults;
    }

    public async Task<IndexStatus> GetStatusAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        string indexDirectory = ResolveIndexDirectory(profileId);
        string manifestPath = Path.Combine(indexDirectory, IndexArtifactNames.Manifest);

        if (!File.Exists(manifestPath))
        {
            return IndexStatus.NotCreated(profileId, indexDirectory);
        }

        try
        {
            DiskIndexManifest manifest = await _indexReader.ReadManifestAsync(indexDirectory, cancellationToken)
                .ConfigureAwait(false);

            return new IndexStatus(
                profileId: profileId,
                exists: true,
                indexDirectory: indexDirectory,
                fileCount: manifest.FileCount,
                chunkCount: manifest.ChunkCount,
                dimension: manifest.Dimension,
                modelId: manifest.ModelId,
                formats: manifest.Formats,
                createdAt: manifest.CreatedAt,
                updatedAt: manifest.UpdatedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read index manifest for Profile={ProfileId} at '{IndexDir}'", profileId, indexDirectory);
            throw new IndexCorruptedException($"Failed to read index status for profile '{profileId}'.", ex);
        }
    }

    private string ResolveIndexDirectory(string profileId)
    {
        string rootBase = !string.IsNullOrWhiteSpace(_sdkOptions.BaseStorageDirectory)
            ? Path.GetFullPath(_sdkOptions.BaseStorageDirectory)
            : Directory.GetCurrentDirectory();

        return Path.Combine(
            rootBase,
            _sdkOptions.EcosystemFolderName,
            _sdkOptions.ProductFolderName,
            profileId,
            InstanciumDirectoryResolver.IndexFolderName);
    }

    private async Task<ProfileSessionHolder> GetOrOpenSessionAsync(
        string profileId,
        string indexDirectory,
        CancellationToken cancellationToken)
    {
        if (_activeSessions.TryGetValue(profileId, out ProfileSessionHolder? existing))
        {
            return existing;
        }

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSessions.TryGetValue(profileId, out existing))
            {
                return existing;
            }

            var vectorStore = new InMemoryVectorStore();
            var session = new VectraIndexSession(_indexReader, vectorStore);
            await session.OpenAsync(indexDirectory, cancellationToken).ConfigureAwait(false);

            var searchEngine = new InMemoryVectorSearchEngine(vectorStore);
            var holder = new ProfileSessionHolder(session, searchEngine);

            _activeSessions[profileId] = holder;
            return holder;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private sealed record ProfileSessionHolder(
        IVectraIndexSession Session,
        IVectorSearchEngine SearchEngine);
}
