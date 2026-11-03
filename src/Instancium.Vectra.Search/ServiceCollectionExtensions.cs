namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// DI registration for the search layer.
/// Search depends only on the IVectorStore contract from Core; the concrete store
/// implementation is registered by the storage layer (AddVectraDiskIndex).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraInMemorySearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IVectorSearchEngine, InMemoryVectorSearchEngine>();
        return services;
    }
}