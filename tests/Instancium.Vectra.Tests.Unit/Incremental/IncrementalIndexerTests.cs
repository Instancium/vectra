#nullable enable
namespace Instancium.Vectra.Tests.Unit.Incremental;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Instancium.Vectra.Indexing.Incremental;
using Instancium.Vectra.Storage.Disk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IncrementalIndexerTests : IDisposable
{
    private const int Dimension = 4;
    private const string RepoId = "test-model";

    private readonly string _testRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-incr-tests", Guid.NewGuid().ToString("N")));
    private readonly string _contentRoot;
    private readonly string _indexDir;

    private readonly FakeFileScanner _scanner = new();
    private readonly ContentExtractorRegistry _extractorRegistry;
    private readonly ChangeClassifier _classifier;
    private readonly FakeTextChunker _chunker = new();
    private readonly FakeInferenceEngine _engine = new(Dimension);
    private readonly DiskIndexReader _reader = new();
    private readonly DiskIndexWriter _writer = new();
    private readonly DiskFileStateStore _fileStateStore = new();

    public IncrementalIndexerTests()
    {
        _contentRoot = Path.Combine(_testRoot, "content");
        _indexDir = Path.Combine(_testRoot, "index");
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(_indexDir);

        _extractorRegistry = new ContentExtractorRegistry(new IContentExtractor[]
        {
            new FakeContentExtractor(".txt")
        });
        _classifier = new ChangeClassifier(_extractorRegistry);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private IncrementalIndexer CreateIndexer()
    {
        return new IncrementalIndexer(
            _scanner,
            _extractorRegistry,
            _classifier,
            _chunker,
            _engine,
            _reader,
            _writer,
            _fileStateStore,
            NullLogger<IncrementalIndexer>.Instance);
    }

    [Fact]
    public async Task IncrementalIndexer_ShouldIndexAllFiles_OnFirstRun()
    {
        string file1 = Path.Combine(_contentRoot, "a.txt");
        string file2 = Path.Combine(_contentRoot, "b.txt");
        File.WriteAllText(file1, "Content A");
        File.WriteAllText(file2, "Content B");

        _scanner.Register(file1, "a.txt", "hash-a", 10);
        _scanner.Register(file2, "b.txt", "hash-b", 20);

        IncrementalIndexer indexer = CreateIndexer();
        IncrementalIndexingResult result = await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        Assert.True(result.IndexWritten);
        Assert.Equal(2, result.Changes.Added.Count);
        Assert.Empty(result.Changes.Modified);
        Assert.Empty(result.Changes.Removed);
        Assert.Equal(2, result.ChunksAdded);
        Assert.Equal(0, result.ChunksDeactivated);
        Assert.Equal(2, result.Manifest.FileCount);
        Assert.Equal(2, result.Manifest.ChunkCount);

        IReadOnlyList<FileState> states = await _fileStateStore.LoadAsync(_indexDir);
        Assert.Equal(2, states.Count);
        Assert.All(states, s => Assert.Equal(IndexFileStatus.Indexed, s.Status));
    }

    [Fact]
    public async Task IncrementalIndexer_ShouldSkipUnchangedFiles_OnSecondRun()
    {
        string file1 = Path.Combine(_contentRoot, "a.txt");
        string file2 = Path.Combine(_contentRoot, "b.txt");
        File.WriteAllText(file1, "Content A");
        File.WriteAllText(file2, "Content B");

        _scanner.Register(file1, "a.txt", "hash-a", 10);
        _scanner.Register(file2, "b.txt", "hash-b", 20);

        IncrementalIndexer indexer = CreateIndexer();
        await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        // Second run with identical scan
        _engine.ResetGenerateCalls();
        IncrementalIndexingResult secondResult = await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        Assert.False(secondResult.IndexWritten);
        Assert.False(secondResult.Changes.HasChanges);
        Assert.Equal(2, secondResult.Changes.Unchanged.Count);
        Assert.Equal(0, _engine.GenerateCalls);
    }

    [Fact]
    public async Task IncrementalIndexer_ShouldReindexOnlyModifiedFile()
    {
        string file1 = Path.Combine(_contentRoot, "a.txt");
        string file2 = Path.Combine(_contentRoot, "b.txt");
        File.WriteAllText(file1, "Content A");
        File.WriteAllText(file2, "Content B");

        _scanner.Register(file1, "a.txt", "hash-a", 10);
        _scanner.Register(file2, "b.txt", "hash-b", 20);

        IncrementalIndexer indexer = CreateIndexer();
        await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        // Modify only b.txt
        File.WriteAllText(file2, "Content B Modified");
        _scanner.Register(file2, "b.txt", "hash-b-v2", 35);

        _engine.ResetGenerateCalls();
        IncrementalIndexingResult modResult = await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        Assert.True(modResult.IndexWritten);
        Assert.Single(modResult.Changes.Modified);
        Assert.Equal("b.txt", modResult.Changes.Modified[0].Current.RelativePath);
        Assert.Single(modResult.Changes.Unchanged);
        Assert.Equal("a.txt", modResult.Changes.Unchanged[0].Path);
        Assert.Equal(1, modResult.ChunksAdded);
        Assert.Equal(1, _engine.GenerateCalls); // Only re-embedded modified chunk
    }

    [Fact]
    public async Task IncrementalIndexer_ShouldRemoveChunksOfDeletedFile()
    {
        string file1 = Path.Combine(_contentRoot, "a.txt");
        string file2 = Path.Combine(_contentRoot, "b.txt");
        File.WriteAllText(file1, "Content A");
        File.WriteAllText(file2, "Content B");

        _scanner.Register(file1, "a.txt", "hash-a", 10);
        _scanner.Register(file2, "b.txt", "hash-b", 20);

        IncrementalIndexer indexer = CreateIndexer();
        await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        // Delete b.txt from scan
        File.Delete(file2);
        _scanner.Remove(file2);

        IncrementalIndexingResult delResult = await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        Assert.True(delResult.IndexWritten);
        Assert.Single(delResult.Changes.Removed);
        Assert.Equal("b.txt", delResult.Changes.Removed[0].Path);
        Assert.Equal(1, delResult.ChunksDeactivated);

        IReadOnlyList<ChunkMapEntry> chunkMap = await _reader.ReadChunkMapAsync(_indexDir);
        ChunkMapEntry bChunk = Assert.Single(chunkMap, c => c.FileId == delResult.Changes.Removed[0].FileId);
        Assert.False(bChunk.IsActive);

        IReadOnlyList<FileState> states = await _fileStateStore.LoadAsync(_indexDir);
        FileState bState = Assert.Single(states, s => s.Path == "b.txt");
        Assert.Equal(IndexFileStatus.Removed, bState.Status);
    }

    [Fact]
    public async Task IncrementalIndexer_ShouldNotTouchIndex_WhenNothingChanged()
    {
        string file1 = Path.Combine(_contentRoot, "a.txt");
        File.WriteAllText(file1, "Content A");
        _scanner.Register(file1, "a.txt", "hash-a", 10);

        IncrementalIndexer indexer = CreateIndexer();
        await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        string manifestPath = Path.Combine(_indexDir, "manifest.json");
        DateTime lastWrite = File.GetLastWriteTimeUtc(manifestPath);

        await Task.Delay(50);
        IncrementalIndexingResult result = await indexer.RunAsync(_contentRoot, _indexDir, RepoId);

        Assert.False(result.IndexWritten);
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(manifestPath));
    }

    private sealed class FakeFileScanner : IFileScanner
    {
        private readonly Dictionary<string, ScannedFile> _files = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string fullPath, string relativePath, string sha256, long size)
        {
            _files[fullPath] = new ScannedFile(fullPath, relativePath, sha256, size);
        }

        public void Remove(string fullPath)
        {
            _files.Remove(fullPath);
        }

        public Task<IReadOnlyList<ScannedFile>> ScanAsync(
            string rootPath,
            IReadOnlyList<string> supportedExtensions,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ScannedFile> list = _files.Values
                .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(list);
        }
    }

    private sealed class FakeContentExtractor : IContentExtractor
    {
        public FakeContentExtractor(string extension)
        {
            SupportedExtensions = new[] { extension };
        }

        public string SourceKind => "text";
        public IReadOnlyList<string> SupportedExtensions { get; }

        public Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
        {
            string content = File.Exists(filePath) ? File.ReadAllText(filePath) : "stub-text";
            var pages = new[] { new PageText(1, content) };
            return Task.FromResult(new ExtractedDocument(filePath, SourceKind, pages));
        }
    }

    private sealed class FakeTextChunker : ITextChunker
    {
        public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages, string sourceFile)
        {
            return pages.Select(p => new TextChunk(p.Text, p.PageNumber, sourceFile)).ToList();
        }
    }

    private sealed class FakeInferenceEngine : IInferenceEngine
    {
        private readonly int _dimension;
        private int _generateCalls;

        public FakeInferenceEngine(int dimension)
        {
            _dimension = dimension;
        }

        public int GenerateCalls => Volatile.Read(ref _generateCalls);

        public void ResetGenerateCalls() => Interlocked.Exchange(ref _generateCalls, 0);

        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
        {
            return Task.FromResult(_dimension);
        }

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string modelId,
            EmbeddingRequest request,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _generateCalls);
            float[] vector = new float[_dimension];
            for (int i = 0; i < _dimension; i++)
            {
                vector[i] = (i + 1) * 0.1f;
            }

            return Task.FromResult(new EmbeddingResponse(vector, _dimension, _dimension));
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
}
