namespace Instancium.Vectra.Storage.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraStorage(this IServiceCollection services, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IInstanciumDirectoryResolver>(_ => new InstanciumDirectoryResolver(baseDirectory));
        services.AddSingleton<IIndexWriter, DiskIndexWriter>();
        services.AddSingleton<IIndexReader, DiskIndexReader>();
        services.AddSingleton<IVectraIndexSession>(provider => new VectraIndexSession(
            provider.GetRequiredService<IIndexReader>(),
            provider.GetRequiredService<IVectorStore>()));

        return services;
    }

    public static IServiceCollection AddVectraDiskIndex(this IServiceCollection services) =>
        services.AddVectraStorage();
}
