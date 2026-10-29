namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;
using Xunit;

[Trait("Category", "Unit")]
public sealed class VectraIndexSessionTests : IDisposable
{
    private const int Dimension = 4;
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-session-tests", Guid.NewGuid().ToString("N")));
    private readonly InstanciumDirectoryResolver _resolver = new();
    private readonly DiskIndexWriter _writer = new();

    public VectraIndexSessionTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task VectraIndexSession_ShouldSearchImmediatelyAfterOpen()
    {
        string indexDirectory = PrepareIndexDirectory();
        IndexWriteRequest request = CreateSampleRequest();
        await _writer.WriteAsync(request, indexDirectory);

        var reader = new DiskIndexReader();
        var vectorStore = new InMemoryVectorStore();
        var session = new VectraIndexSession(reader, vectorStore);

        await session.OpenAsync(indexDirectory);

        var searchEngine = new InMemoryVectorSearchEngine(session.VectorStore);
        // Search by the exact unit vector of chunk 2: [0.0, 1.0, 0.0, 0.0]
        float[] queryVector = new[] { 0.0f, 1.0f, 0.0f, 0.0f };

        IReadOnlyList<SearchResult> results = await searchEngine.SearchAsync(queryVector, topK: 3);

        Assert.NotEmpty(results);
        SearchResult top1 = results[0];
        Assert.Equal(2, top1.ChunkId);
        Assert.InRange(top1.Score, 0.999f, 1.001f);
    }

    private string PrepareIndexDirectory()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root, Path.Combine(_root, "idx"));
        _resolver.EnsureDirectoryStructure(indexDirectory);
        return indexDirectory;
    }

    private static IndexWriteRequest CreateSampleRequest()
    {
        var manifest = new DiskIndexManifest(
            version: DiskIndexManifest.CurrentVersion,
            dimension: Dimension,
            modelId: "stub-model",
            formats: new[] { "pdf" },
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            fileCount: 2,
            chunkCount: 3);

        FileState[] files =
        {
            new(1, "docs/manual.pdf", "aaaa", 100, "pdf", new long[] { 1, 2 }, CreatedAt, IndexFileStatus.Indexed),
            new(2, "docs/guide.pdf", "bbbb", 200, "pdf", new long[] { 3 }, CreatedAt, IndexFileStatus.Indexed)
        };

        ChunkRecord[] chunks =
        {
            new(1, 1, 1, "chunk text 1", new float[] { 1.0f, 0.0f, 0.0f, 0.0f }),
            new(2, 1, 2, "chunk text 2", new float[] { 0.0f, 1.0f, 0.0f, 0.0f }),
            new(3, 2, 1, "chunk text 3", new float[] { 0.0f, 0.0f, 1.0f, 0.0f })
        };

        return new IndexWriteRequest(manifest, files, chunks);
    }
}
