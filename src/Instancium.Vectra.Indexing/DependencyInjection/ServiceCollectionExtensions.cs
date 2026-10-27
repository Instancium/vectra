namespace Instancium.Vectra.Indexing.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Indexing.Chunking;
using Instancium.Vectra.Indexing.Extraction;
using Instancium.Vectra.Indexing.Pipeline;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextChunker>(_ => new PageAwareFixedSizeChunker());
        services.AddSingleton<IIndexingPipeline, IndexingPipeline>();
        return services;
    }
}