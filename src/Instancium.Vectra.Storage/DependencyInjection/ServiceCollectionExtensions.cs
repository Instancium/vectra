namespace Instancium.Vectra.Storage.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Storage.Disk;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraDiskIndex(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IInstanciumDirectoryResolver, InstanciumDirectoryResolver>();
        services.AddSingleton<IIndexWriter, DiskIndexWriter>();
        return services;
    }
}
