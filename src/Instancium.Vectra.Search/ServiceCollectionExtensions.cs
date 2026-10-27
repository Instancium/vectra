namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Storage;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraInMemorySearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        services.AddSingleton<IVectorSearchEngine, InMemoryVectorSearchEngine>();

        return services;
    }
}

