namespace Instancium.Vectra.Indexing.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Indexing.Chunking;
using Instancium.Vectra.Indexing.Extraction;
using Instancium.Vectra.Indexing.Incremental;
using Instancium.Vectra.Indexing.Pipeline;
using Instancium.Vectra.Indexing.Scanning;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraIndexing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();

        // Single PdfContentExtractor instance serves both contracts:
        // IContentExtractor (full-file extraction, stage 4+) and
        // ITextExtractor (page-range extraction used by existing pipeline and CLI bench).
        services.AddSingleton<PdfContentExtractor>();
        services.AddSingleton<ITextExtractor>(provider => provider.GetRequiredService<PdfContentExtractor>());
        services.AddSingleton<IContentExtractor>(provider => provider.GetRequiredService<PdfContentExtractor>());

        services.AddSingleton<IContentExtractorRegistry, ContentExtractorRegistry>();
        services.AddSingleton<IFileScanner, DirectoryFileScanner>();
        services.AddSingleton<IChangeClassifier, ChangeClassifier>();

        services.AddSingleton<ITextChunker>(_ => new PageAwareFixedSizeChunker());
        services.AddSingleton<IIndexingPipeline, IndexingPipeline>();

        return services;
    }
}
