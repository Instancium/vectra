namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IVectorSearchEngine
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK, CancellationToken cancellationToken = default);
}
