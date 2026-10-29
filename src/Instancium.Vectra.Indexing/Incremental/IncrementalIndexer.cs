namespace Instancium.Vectra.Indexing.Incremental;

using System.Diagnostics;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class IncrementalIndexer : IIncrementalIndexer
{
    private readonly IFileScanner _scanner;
    private readonly IContentExtractorRegistry _registry;
    private readonly IChangeClassifier _classifier;
    private readonly ITextChunker _chunker;
    private readonly IInferenceEngine _engine;
    private readonly IIndexReader _reader;
    private readonly IIndexWriter _writer;
    private readonly IFileStateStore _fileStateStore;
    private readonly ILogger<IncrementalIndexer> _logger;

    public IncrementalIndexer(
        IFileScanner scanner,
        IContentExtractorRegistry registry,
        IChangeClassifier classifier,
        ITextChunker chunker,
        IInferenceEngine engine,
        IIndexReader reader,
        IIndexWriter writer,
        IFileStateStore fileStateStore,
        ILogger<IncrementalIndexer>? logger = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _fileStateStore = fileStateStore ?? throw new ArgumentNullException(nameof(fileStateStore));
        _logger = logger ?? NullLogger<IncrementalIndexer>.Instance;
    }

    public async Task<IncrementalIndexingResult> RunAsync(
        string contentRoot,
        string indexDirectory,
        string repoId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        cancellationToken.ThrowIfCancellationRequested();

        string fullContentRoot = Path.GetFullPath(contentRoot);
        string fullIndexDirectory = Path.GetFullPath(indexDirectory);

        if (!Directory.Exists(fullIndexDirectory))
        {
            throw new DirectoryNotFoundException($"Index directory '{fullIndexDirectory}' does not exist.");
        }

        string manifestPath = Path.Combine(fullIndexDirectory, "manifest.json");
        bool isFirstRun = !File.Exists(manifestPath);

        DiskIndexManifest? existingManifest = null;
        IReadOnlyList<FileState> previousStates = Array.Empty<FileState>();
        IReadOnlyList<ChunkMapEntry> previousChunkMap = Array.Empty<ChunkMapEntry>();
        ReadOnlyMemory<float> previousVectors = ReadOnlyMemory<float>.Empty;

        if (!isFirstRun)
        {
            existingManifest = await _reader.ReadManifestAsync(fullIndexDirectory, cancellationToken).ConfigureAwait(false);
            previousStates = await _fileStateStore.LoadAsync(fullIndexDirectory, cancellationToken).ConfigureAwait(false);
            previousChunkMap = await _reader.ReadChunkMapAsync(fullIndexDirectory, cancellationToken).ConfigureAwait(false);
            previousVectors = await _reader.ReadVectorsAsync(fullIndexDirectory, cancellationToken).ConfigureAwait(false);
        }

        int dimension = await _engine.GetEmbeddingDimensionAsync(repoId, cancellationToken).ConfigureAwait(false);
        if (dimension <= 0)
        {
            throw new IndexingFailedException(
                $"Gateway returned invalid dimension {dimension} for repo '{repoId}'.");
        }

        if (existingManifest is not null)
        {
            if (!string.Equals(existingManifest.ModelId, repoId, StringComparison.Ordinal))
            {
                throw new IndexingFailedException(
                    $"Existing index was created with model '{existingManifest.ModelId}', but request specified '{repoId}'. " +
                    "Rebuild the index from scratch after changing the embedding model.");
            }

            if (existingManifest.Dimension != dimension)
            {
                throw new IndexingFailedException(
                    $"Existing index dimension is {existingManifest.Dimension}, but model '{repoId}' has dimension {dimension}. " +
                    "Rebuild the index from scratch after changing the embedding model.");
            }
        }

        IReadOnlyList<ScannedFile> scannedFiles = await _scanner
            .ScanAsync(fullContentRoot, _registry.SupportedExtensions, cancellationToken)
            .ConfigureAwait(false);
        ChangeSet changes = _classifier.Classify(scannedFiles, previousStates);

        if (!changes.HasChanges && existingManifest is not null)
        {
            _logger.LogInformation("No changes detected in {ContentRoot}. Index on disk left untouched.", fullContentRoot);
            return new IncrementalIndexingResult(
                changes,
                existingManifest,
                ChunksAdded: 0,
                ChunksDeactivated: 0,
                IndexWritten: false);
        }

        long nextFileId = 1L;
        long nextChunkId = 1L;
        if (previousStates.Count > 0)
        {
            nextFileId = previousStates.Max(s => s.FileId) + 1L;
        }
        if (previousChunkMap.Count > 0)
        {
            nextChunkId = previousChunkMap.Max(c => c.ChunkId) + 1L;
        }

        var survivingChunks = new List<ChunkRecord>();
        var survivingFiles = new List<FileState>();
        var modifiedPreviousFileIds = changes.Modified.Select(m => m.Previous.FileId).ToHashSet();
        var removedFileIds = changes.Removed.Select(r => r.FileId).ToHashSet();

        // 1. Unchanged files and their chunks
        foreach (FileState unchanged in changes.Unchanged)
        {
            survivingFiles.Add(unchanged);
        }

        // Previous chunks for unchanged files
        var unchangedFileIds = changes.Unchanged.Select(u => u.FileId).ToHashSet();
        for (int i = 0; i < previousChunkMap.Count; i++)
        {
            ChunkMapEntry entry = previousChunkMap[i];
            if (unchangedFileIds.Contains(entry.FileId))
            {
                string text = await _reader.ReadChunkTextAsync(
                    fullIndexDirectory, entry.Offset, entry.Length, cancellationToken).ConfigureAwait(false);
                ReadOnlyMemory<float> vector = previousVectors.Slice(i * dimension, dimension);
                survivingChunks.Add(new ChunkRecord(entry.ChunkId, entry.FileId, entry.PageNumber, text, vector));
            }
        }

        // 2. Removed files: mark Removed and carry over chunks as inactive
        int chunksDeactivated = 0;
        foreach (FileState removed in changes.Removed)
        {
            FileState updatedRemovedState = new(
                removed.FileId,
                removed.Path,
                removed.Sha256,
                removed.SizeBytes,
                removed.Format,
                removed.ChunkIds,
                removed.IndexedAt,
                IndexFileStatus.Removed);
            survivingFiles.Add(updatedRemovedState);
        }

        for (int i = 0; i < previousChunkMap.Count; i++)
        {
            ChunkMapEntry entry = previousChunkMap[i];
            if (removedFileIds.Contains(entry.FileId))
            {
                string text = await _reader.ReadChunkTextAsync(
                    fullIndexDirectory, entry.Offset, entry.Length, cancellationToken).ConfigureAwait(false);
                ReadOnlyMemory<float> vector = previousVectors.Slice(i * dimension, dimension);
                survivingChunks.Add(new ChunkRecord(entry.ChunkId, entry.FileId, entry.PageNumber, text, vector, isActive: false));
                chunksDeactivated++;
            }
        }

        // 3. Process Added and Modified files
        var filesToProcess = new List<(ScannedFile Scanned, long FileId)>();
        foreach (ScannedFile added in changes.Added)
        {
            filesToProcess.Add((added, nextFileId++));
        }
        foreach (ModifiedFile modified in changes.Modified)
        {
            filesToProcess.Add((modified.Current, modified.Previous.FileId));
        }

        int chunksAdded = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (var (scanned, fileId) in filesToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IContentExtractor extractor = _registry.Resolve(Path.GetExtension(scanned.FullPath));
            ExtractedDocument document = await extractor.ExtractAsync(scanned.FullPath, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<TextChunk> textChunks = _chunker.Chunk(document.Pages, scanned.FullPath);

            var chunkRecords = new ChunkRecord[textChunks.Count];
            var newChunkIds = new long[textChunks.Count];

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(
                textChunks.Select((chunk, index) => (chunk, index)),
                parallelOptions,
                async (item, token) =>
                {
                    var (chunk, index) = item;
                    var response = await _engine.GenerateEmbeddingsAsync(
                        repoId,
                        new EmbeddingRequest(chunk.Text, Normalize: true),
                        token).ConfigureAwait(false);

                    long assignedChunkId = Interlocked.Increment(ref nextChunkId) - 1L;
                    newChunkIds[index] = assignedChunkId;
                    chunkRecords[index] = new ChunkRecord(
                        assignedChunkId,
                        fileId,
                        chunk.PageNumber,
                        chunk.Text,
                        response.Embedding);
                }).ConfigureAwait(false);

            Array.Sort(newChunkIds);
            var sortedChunkRecords = chunkRecords.OrderBy(c => c.ChunkId).ToList();

            survivingChunks.AddRange(sortedChunkRecords);
            chunksAdded += sortedChunkRecords.Count;

            FileState state = new(
                fileId,
                scanned.RelativePath,
                scanned.Sha256,
                scanned.SizeBytes,
                extractor.SourceKind,
                sortedChunkRecords.Select(c => c.ChunkId).ToArray(),
                now,
                IndexFileStatus.Indexed);

            survivingFiles.Add(state);
        }

        var allSupportedFormats = survivingFiles
            .Select(f => f.Format)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DateTimeOffset createdAt = existingManifest?.CreatedAt ?? now;

        var finalManifest = new DiskIndexManifest(
            DiskIndexManifest.CurrentVersion,
            dimension,
            repoId,
            allSupportedFormats,
            createdAt,
            now,
            fileCount: survivingFiles.Count,
            chunkCount: survivingChunks.Count);

        var writeRequest = new IndexWriteRequest(
            finalManifest,
            survivingFiles,
            survivingChunks);

        await _writer.WriteAsync(writeRequest, fullIndexDirectory, cancellationToken).ConfigureAwait(false);

        return new IncrementalIndexingResult(
            changes,
            finalManifest,
            chunksAdded,
            chunksDeactivated,
            IndexWritten: true);
    }
}
