#nullable enable
namespace Instancium.Vectra.Core.Models;

public sealed record RagRequest
{
    public RagRequest(string question, string embeddingRepoId, int topK = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingRepoId);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        Question = question;
        EmbeddingRepoId = embeddingRepoId;
        TopK = topK;
    }

    public string Question { get; }

    public string EmbeddingRepoId { get; }

    public int TopK { get; }
}
