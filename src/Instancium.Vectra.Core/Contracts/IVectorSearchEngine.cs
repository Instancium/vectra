namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// In-memory vector search over the current store contents.
/// Interim shape: returns the v2 hit type. In P4 this contract becomes a thin wrapper
/// over ISearchKernel + IVectraSnapshot.
/// </summary>
public interface IVectorSearchEngine
{
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK, CancellationToken cancellationToken = default);
}