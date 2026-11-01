namespace Instancium.Vectra.Sdk.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Sdk.Configuration;
using Instancium.Vectra.Sdk.Contracts;
using Instancium.Vectra.Sdk.Engine;
using Instancium.Vectra.Storage.Disk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core, storage, indexing, and infrastructure dependencies required by Vectra Engine.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root containing Vectra gateway settings.</param>
    /// <param name="configureSdk">Optional action to configure SDK specific options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddVectraEngine(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<VectraSdkOptions>? configureSdk = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddLogging();

        // 1. SDK Options
        services.AddOptions<VectraSdkOptions>()
            .Bind(configuration.GetSection(VectraSdkOptions.SectionName));

        if (configureSdk is not null)
        {
            services.Configure(configureSdk);
        }

        // 2. Infrastructure Layer (IInferenceEngine via SignalR Gateway)
        services.AddVectraInfrastructure(configuration);

        // 3. Storage Layer (Disk persistence contracts)
        services.AddSingleton<IInstanciumDirectoryResolver, InstanciumDirectoryResolver>();
        services.AddSingleton<IIndexWriter, DiskIndexWriter>();
        services.AddSingleton<IIndexReader, DiskIndexReader>();
        services.AddSingleton<IFileStateStore, DiskFileStateStore>();

        // 4. Indexing Layer (Scanner, Extractors, Chunker, Pipeline, IncrementalIndexer)
        services.AddVectraIndexing();

        // 5. High-Level Engine Facade
        services.AddSingleton<IVectraEngine, VectraEngine>();

        return services;
    }
}