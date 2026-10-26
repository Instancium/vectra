namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IEmbeddingGenerationService
{
    Task<EmbeddingResult> GenerateAsync(string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmbeddingResult>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    Task<ModelMetadata> GetModelMetadataAsync(CancellationToken cancellationToken = default);
}
