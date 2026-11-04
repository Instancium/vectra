#nullable enable
namespace Instancium.Vectra.Search;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search.Kernels;

internal sealed class VectraCollection : IVectraCollection
{
    private readonly string _name;
    private readonly CollectionOptions _options;
    private readonly IVectorStore _store;
    private readonly IVectraIndexSession _session;
    private readonly IIndexWriter _writer;
    private readonly string _indexDirectory;
    private readonly VectraOptions _globalOptions;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _maxId;

    public VectraCollection(
        string name,
        CollectionOptions options,
        IVectorStore store,
        IVectraIndexSession session,
        IIndexWriter writer,
        string indexDirectory,
        VectraOptions globalOptions)
    {
        _name = name;
        _options = options;
        _store = store;
        _session = session;
        _writer = writer;
        _indexDirectory = indexDirectory;
        _globalOptions = globalOptions;

        var meta = _store.GetMetadata();
        foreach (var m in meta)
        {
            if (m.ChunkId > _maxId)
            {
                _maxId = m.ChunkId;
            }
        }
    }

    public string Name => _name;

    public CollectionInfo Info => new(
        _name, _options.Dimension, _options.Metric, _options.ModelId, _options.ModelFingerprint,
        _store.Count, DiskIndexManifest.CurrentVersion);

    public async ValueTask<long> AddAsync(VectorEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long id = entry.VectorId ?? ++_maxId;
            if (entry.VectorId.HasValue && entry.VectorId.Value > _maxId)
            {
                _maxId = entry.VectorId.Value;
            }

            if (entry.Vector.Length != _options.Dimension)
            {
                throw new VectraDimensionMismatchException(_options.Dimension, entry.Vector.Length);
            }

            ReadOnlyMemory<float> vectorToStore = _options.Metric == SimilarityMetric.Cosine
                ? NormalizeCosine(entry.Vector)
                : entry.Vector;

            _store.Upsert(id, 1, 1, entry.Payload ?? string.Empty, vectorToStore);
            await FlushToDiskAsync(cancellationToken).ConfigureAwait(false);

            return id;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task AddRangeAsync(ReadOnlyMemory<VectorEntry> entries, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var span = entries.Span;
            for (int i = 0; i < span.Length; i++)
            {
                var entry = span[i];
                long id = entry.VectorId ?? ++_maxId;
                if (entry.VectorId.HasValue && entry.VectorId.Value > _maxId)
                {
                    _maxId = entry.VectorId.Value;
                }

                if (entry.Vector.Length != _options.Dimension)
                {
                    throw new VectraDimensionMismatchException(_options.Dimension, entry.Vector.Length);
                }

                ReadOnlyMemory<float> vectorToStore = _options.Metric == SimilarityMetric.Cosine
                    ? NormalizeCosine(entry.Vector)
                    : entry.Vector;

                _store.Upsert(id, 1, 1, entry.Payload ?? string.Empty, vectorToStore);
            }
            await FlushToDiskAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask<bool> RemoveAsync(long vectorId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool removed = _store.Remove(vectorId);
            if (removed)
            {
                await FlushToDiskAsync(cancellationToken).ConfigureAwait(false);
            }
            return removed;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        SearchQuery query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SearchOptions();

        if (query.Filter is not null)
        {
            throw new NotSupportedException("Metadata filtering is reserved for future implementation. IExpression evaluator is missing.");
        }

        if (query.Vector.Length != _options.Dimension)
        {
            throw new VectraDimensionMismatchException(_options.Dimension, query.Vector.Length);
        }

        IVectraSnapshot snapshot = _store.CreateSnapshot(_options.Metric);

        // P8 Seam: Resolves managed SIMD or native external kernel via VectraOptions
        ISimilarityKernel similarityKernel = SimilarityKernelFactory.Create(
            _options.Metric,
            _globalOptions.Kernel,
            _globalOptions.CustomKernelResolver);

        ISearchKernel kernel = new BruteForceSearchKernel(similarityKernel);

        var normalizedQuery = query;
        if (_options.Metric == SimilarityMetric.Cosine)
        {
            normalizedQuery = new SearchQuery(NormalizeCosine(query.Vector), query.QueryText, query.Filter);
        }

        var candidates = await kernel.SearchAsync(snapshot, normalizedQuery, options, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<VectorSearchHit> finalHits = candidates;
        double rerankTimeMs = 0.0;

        if (options.Reranker is not null)
        {
            var sw = Stopwatch.StartNew();
            finalHits = await options.Reranker.RerankAsync(query, candidates, cancellationToken).ConfigureAwait(false);
            rerankTimeMs = sw.Elapsed.TotalMilliseconds;

            var candidateIds = new HashSet<long>(candidates.Select(c => c.VectorId));

            foreach (var hit in finalHits)
            {
                if (!candidateIds.Contains(hit.VectorId))
                {
                    throw new InvalidOperationException($"Reranker returned a vector id {hit.VectorId} that was not present in the candidate set.");
                }
            }
        }

        int takeCount = Math.Min(finalHits.Count, options.TopK);
        var result = new List<VectorSearchHit>(takeCount);

        foreach (var hit in finalHits.Take(takeCount))
        {
            string? payload = await GetPayloadTextAsync(hit.VectorId, cancellationToken).ConfigureAwait(false);

            SearchDiagnostics? diag = null;
            if (options.IncludeDiagnostics)
            {
                var recallDiag = hit.Diagnostics;
                diag = new SearchDiagnostics(
                    recallDiag?.ScannedVectors ?? 0,
                    recallDiag?.RecallTimeMs ?? 0.0,
                    rerankTimeMs,
                    recallDiag?.KernelUsed);
            }

            result.Add(new VectorSearchHit(
                hit.VectorId,
                hit.VectorScore,
                options.Reranker is null ? hit.VectorScore : hit.FinalScore,
                payload,
                diag));
        }

        return result;
    }

    public async ValueTask<string?> GetPayloadTextAsync(long vectorId, CancellationToken cancellationToken = default)
    {
        string? inMemory = _store.GetPayload(vectorId);
        if (!string.IsNullOrEmpty(inMemory))
        {
            return inMemory;
        }

        try
        {
            return await _session.GetChunkTextAsync(vectorId, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static ReadOnlyMemory<float> NormalizeCosine(ReadOnlyMemory<float> vector)
    {
        double sumOfSquares = 0.0;
        ReadOnlySpan<float> span = vector.Span;
        for (int i = 0; i < span.Length; i++)
        {
            sumOfSquares += (double)span[i] * span[i];
        }
        float norm = (float)Math.Sqrt(sumOfSquares);
        if (norm < 1e-12f) return vector;

        float[] normalized = new float[span.Length];
        for (int i = 0; i < span.Length; i++)
        {
            normalized[i] = span[i] / norm;
        }
        return normalized;
    }

    private async Task FlushToDiskAsync(CancellationToken ct)
    {
        var manifest = new DiskIndexManifest(
            DiskIndexManifest.CurrentVersion,
            _options.Dimension,
            _options.ModelId,
            new[] { "vectra-api" },
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            fileCount: 1,
            chunkCount: _store.Count);

        var metadata = _store.GetMetadata();
        float[] allVectors = _store.GetVectorsSpan().ToArray(); 
        var chunks = new ChunkRecord[metadata.Count];

        for (int i = 0; i < metadata.Count; i++)
        {
            var meta = metadata[i];
            var vectorSpan = new ReadOnlyMemory<float>(allVectors, i * _options.Dimension, _options.Dimension);

            string? payload = meta.Payload;
            if (string.IsNullOrEmpty(payload))
            {
                try { payload = await _session.GetChunkTextAsync(meta.ChunkId, ct).ConfigureAwait(false); }
                catch { payload = string.Empty; }
            }

            chunks[i] = new ChunkRecord(meta.ChunkId, meta.FileId, meta.PageNumber, payload ?? string.Empty, vectorSpan, true);
        }

        var request = new IndexWriteRequest(manifest, chunks);
        await _writer.WriteAsync(request, _indexDirectory, ct).ConfigureAwait(false);
    }
}