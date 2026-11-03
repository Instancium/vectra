namespace Instancium.Vectra.Storage.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// DI registration for the storage layer: in-memory vector store, on-disk index
/// reader/writer, directory resolver and index session.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraDiskIndex(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IVectorStore, InMemoryVectorStore>();
        services.TryAddSingleton<IInstanciumDirectoryResolver, InstanciumDirectoryResolver>();
        services.TryAddSingleton<IIndexWriter, DiskIndexWriter>();
        services.TryAddSingleton<IIndexReader, DiskIndexReader>();
        services.TryAddSingleton<IVectraIndexSession>(provider => new VectraIndexSession(
            provider.GetRequiredService<IIndexReader>(),
            provider.GetRequiredService<IVectorStore>()));
        return services;
    }
}