namespace Instancium.Vectra.Rag.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraRag(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRagService, RagService>();
        return services;
    }
}
