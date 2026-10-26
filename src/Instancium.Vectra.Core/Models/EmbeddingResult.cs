namespace Instancium.Vectra.Core.Models;

public sealed record EmbeddingResult(
    float[] Vector,
    int TokenCount,
    int Dimension);
