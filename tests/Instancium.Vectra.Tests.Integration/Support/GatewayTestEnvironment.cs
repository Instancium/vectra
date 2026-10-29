namespace Instancium.Vectra.Tests.Integration.Support;

using System;
using System.IO;
using System.Net.Http;
using Instancium.Vectra.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

public static class GatewayTestEnvironment
{
    private static readonly IConfiguration Configuration;

    static GatewayTestEnvironment()
    {
        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddVectraConfiguration("config_vectra.json")
            .AddEnvironmentVariables()
            .Build();
    }

    public const string GatewayUrlVariable = "VECTRA_EMBEDDING_GATEWAY_URL";
    public const string ApiKeyVariable = "VECTRA_GATEWAY_API_KEY";
    public const string CorpusVariable = "VECTRA_TEST_CORPUS_PDF";
    public const string RequireVariable = "VECTRA_REQUIRE_GATEWAY";

    public static string GatewayUrl =>
        Environment.GetEnvironmentVariable(GatewayUrlVariable)
        ?? Configuration["Vectra:Gateways:Embedding:Url"]
        ?? "http://localhost:5001";

    public static string? ApiKey =>
        Environment.GetEnvironmentVariable(ApiKeyVariable)
        ?? Configuration["Vectra:Gateways:Embedding:ApiKey"];

    public static string? CorpusPath =>
        Environment.GetEnvironmentVariable(CorpusVariable)
        ?? Configuration["Vectra:TestCorpus:PdfPath"];

    public static bool RequireGateway =>
        Environment.GetEnvironmentVariable(RequireVariable) == "1"
        || Configuration["VECTRA_REQUIRE_GATEWAY"] == "1";

    private static readonly object Sync = new();
    private static string? _skipReason;
    private static bool _evaluated;

    public static string? GetSkipReason(bool requiresCorpus)
    {
        if (RequireGateway)
        {
            return null;
        }

        lock (Sync)
        {
            if (!_evaluated)
            {
                _skipReason = EvaluateGateway();
                _evaluated = true;
            }
        }

        if (_skipReason is not null)
        {
            return _skipReason;
        }

        if (requiresCorpus)
        {
            string? corpus = CorpusPath;
            if (string.IsNullOrWhiteSpace(corpus))
            {
                return $"Set {CorpusVariable} or configure Vectra:TestCorpus:PdfPath in appsettings.json";
            }

            if (!File.Exists(corpus))
            {
                return $"Corpus PDF not found at '{corpus}'.";
            }
        }

        return null;
    }

    private static string? EvaluateGateway()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return $"Set {ApiKeyVariable} or configure Vectra:Gateways:Embedding:ApiKey in appsettings.json";
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using HttpResponseMessage response = client
                .GetAsync($"{GatewayUrl}/health/ready")
                .GetAwaiter()
                .GetResult();
            return response.IsSuccessStatusCode
                ? null
                : $"Embedding gateway readiness probe returned {(int)response.StatusCode} at {GatewayUrl}/health/ready.";
        }
        catch (Exception ex)
        {
            return $"Embedding gateway unreachable at {GatewayUrl}/health/ready ({ex.GetType().Name}).";
        }
    }
}

public sealed class GatewayFactAttribute : FactAttribute
{
    public GatewayFactAttribute(bool requiresCorpus = false)
    {
        Skip = GatewayTestEnvironment.GetSkipReason(requiresCorpus);
    }
}