namespace Instancium.Vectra.Core.Models;

public sealed record RagRetrievalResult(string Question, IReadOnlyList<RagContextChunk> Context);
