namespace Instancium.Vectra.Infrastructure.DependencyInjection;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Infrastructure.Chat;
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

    public static IServiceCollection AddVectraChatInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ChatGatewayOptions>(configuration.GetSection(ChatGatewayOptions.SectionName));
        services.AddSingleton<IChatTransport, SignalRChatTransport>();
        services.AddSingleton<ChatContractProbe>(sp =>
            new ChatContractProbe(
                sp.GetRequiredService<IChatTransport>(),
                sp.GetRequiredService<IOptions<ChatGatewayOptions>>(),
                sp.GetRequiredService<ILogger<ChatContractProbe>>()));
        services.AddSingleton<IChatCompletionClient>(sp =>
            new InstantAIGateChatAdapter(
                sp.GetRequiredService<IChatTransport>(),
                sp.GetRequiredService<IOptions<ChatGatewayOptions>>(),
                sp.GetRequiredService<ILogger<InstantAIGateChatAdapter>>()));
        return services;
    }
}
