namespace Instancium.Vectra.Core.Models;

public sealed record IndexManifest(
    string IndexName,
    string Version,
    int Dimension,
    string ModelId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<long> ChunkIds);
