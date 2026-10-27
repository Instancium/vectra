#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record IndexingPipelineRequest
{
    public IndexingPipelineRequest(
        string pdfPath,
        int pageFrom,
        int pageTo,
        string repoId,
        TextChunkingOptions? chunkingOptions = null,
        int maxParallelEmbeddings = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageFrom, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageTo, pageFrom);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelEmbeddings, 1);

        PdfPath = pdfPath;
        PageFrom = pageFrom;
        PageTo = pageTo;
        RepoId = repoId;
        ChunkingOptions = chunkingOptions ?? new TextChunkingOptions();
        MaxParallelEmbeddings = maxParallelEmbeddings;
    }

    public string PdfPath { get; }

    public int PageFrom { get; }

    public int PageTo { get; }

    public string RepoId { get; }

    public TextChunkingOptions ChunkingOptions { get; }

    public int MaxParallelEmbeddings { get; }
}
