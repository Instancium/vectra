namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IRagService
{
    Task<RagRetrievalResult> RetrieveAsync(RagRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAnswerAsync(RagRetrievalResult retrieval, CancellationToken cancellationToken = default);
}
