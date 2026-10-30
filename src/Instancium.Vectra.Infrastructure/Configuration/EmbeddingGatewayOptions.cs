namespace Instancium.Vectra.Infrastructure.Configuration;

public sealed class EmbeddingGatewayOptions
{
    public const string SectionName = "Vectra:Gateways:Embedding";

    public string Url { get; set; } = "http://localhost:5000";

    public string HubPath { get; set; } = "/hub/gateway";

    public string RepoId { get; set; } = "qwen3-embedding-0.6b";

    public int MaxParallelRequests { get; set; } = 4;

    public string ApiKey { get; set; } = string.Empty;

    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxRetryAttempts { get; set; } = 3;
}
