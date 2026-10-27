namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IIndexingPipeline
{
    Task<IndexingPipelineResult> RunAsync(IndexingPipelineRequest request, CancellationToken ct = default);
}
