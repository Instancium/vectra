#nullable enable
namespace Instancium.Vectra.Search;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;

internal sealed class VectraEngine : IVectraEngine
{
    private readonly VectraOptions _options;
    private readonly IInstanciumDirectoryResolver _resolver;
    private readonly IIndexReader _reader;
    private readonly IIndexWriter _writer;
    private readonly ConcurrentDictionary<string, IVectraCollection> _collections = new(StringComparer.OrdinalIgnoreCase);

    public VectraEngine(
        VectraOptions options,
        IInstanciumDirectoryResolver resolver,
        IIndexReader reader,
        IIndexWriter writer)
    {
        _options = options;
        _resolver = resolver;
        _reader = reader;
        _writer = writer;
    }

    public IVectraCollection GetOrOpenCollection(string name, CollectionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        return _collections.GetOrAdd(name, key =>
        {
            string indexDir = _resolver.ResolveIndexDirectory(_options.BaseDirectory, Path.Combine(_options.BaseDirectory, key));

            DiskIndexManifest? existingManifest = null;
            string manifestPath = Path.Combine(indexDir, IndexArtifactNames.Manifest);

            if (Directory.Exists(indexDir) && File.Exists(manifestPath))
            {
                existingManifest = _reader.ReadManifestAsync(indexDir).GetAwaiter().GetResult();

                if (existingManifest.Dimension != options.Dimension)
                {
                    throw new VectraDimensionMismatchException(options.Dimension, existingManifest.Dimension);
                }

                if (!string.Equals(existingManifest.ModelId, options.ModelId, StringComparison.Ordinal))
                {
                    throw new VectraIncompatibleModelException(options.ModelId, existingManifest.ModelId);
                }
            }
            else
            {
                _resolver.EnsureDirectoryStructure(indexDir);
                var manifest = new DiskIndexManifest(
                    DiskIndexManifest.CurrentVersion, options.Dimension, options.ModelId,
                    new[] { "vectra-api" }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0);

                _writer.WriteAsync(new IndexWriteRequest(manifest, Array.Empty<ChunkRecord>()), indexDir).GetAwaiter().GetResult();
            }

            var store = new InMemoryVectorStore();
            var session = new VectraIndexSession(_reader, store, options.Dimension, options.ModelId);
            session.OpenAsync(indexDir).GetAwaiter().GetResult();

            return new VectraCollection(key, options, store, session, _writer, indexDir, _options);
        });
    }

    public async Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_options.BaseDirectory))
        {
            return Array.Empty<CollectionInfo>();
        }

        var dirs = Directory.GetDirectories(_options.BaseDirectory);
        var results = new List<CollectionInfo>();

        foreach (var dir in dirs)
        {
            string manifestPath = Path.Combine(dir, IndexArtifactNames.Manifest);
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = await _reader.ReadManifestAsync(dir, cancellationToken).ConfigureAwait(false);
                    results.Add(new CollectionInfo(
                        Path.GetFileName(dir), manifest.Dimension, _options.DefaultMetric,
                        manifest.ModelId, null, manifest.ChunkCount, manifest.Version));
                }
                catch
                {
                    // Skip corrupted or unreadable collections
                }
            }
        }

        return results;
    }

    public Task DeleteCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        _collections.TryRemove(name, out _);

        string indexDir = _resolver.ResolveIndexDirectory(_options.BaseDirectory, Path.Combine(_options.BaseDirectory, name));
        if (Directory.Exists(indexDir))
        {
            Directory.Delete(indexDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _collections.Clear();
        return default;
    }
}