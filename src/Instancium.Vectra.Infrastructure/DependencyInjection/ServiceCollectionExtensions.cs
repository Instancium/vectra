namespace Instancium.Vectra.Infrastructure.DependencyInjection;

using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmbeddingGatewayOptions>(
            configuration.GetSection(EmbeddingGatewayOptions.SectionName));

        services.AddSingleton<IEmbeddingTransport, SignalREmbeddingTransport>();
        services.AddSingleton<IInferenceEngine>(sp =>
            new InstantAIGateEmbeddingAdapter(
                sp.GetRequiredService<IEmbeddingTransport>(),
                sp.GetRequiredService<IOptions<EmbeddingGatewayOptions>>(),
                sp.GetRequiredService<ILogger<InstantAIGateEmbeddingAdapter>>()
            ));

        return services;
    }
}