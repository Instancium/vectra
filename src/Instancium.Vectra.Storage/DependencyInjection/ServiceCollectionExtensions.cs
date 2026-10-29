namespace Instancium.Vectra.Storage.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraDiskIndex(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IInstanciumDirectoryResolver, InstanciumDirectoryResolver>();
        services.AddSingleton<IIndexWriter, DiskIndexWriter>();
        services.AddSingleton<IIndexReader, DiskIndexReader>();
        services.AddSingleton<IVectraIndexSession>(provider => new VectraIndexSession(
            provider.GetRequiredService<IIndexReader>(),
            provider.GetRequiredService<IVectorStore>()));

        return services;
    }
}
