namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IIncrementalIndexer
{
    Task<IncrementalIndexingResult> RunAsync(
        string contentRoot,
        string indexDirectory,
        string repoId,
        CancellationToken cancellationToken = default);
}
