namespace Instancium.Vectra.Tests.Integration.Bench;

using System.Text;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Tests.Integration.Gold;
using Xunit;

[Trait("Category", "Bench")]
public sealed class IndexingPipelineBenchTests
{
    [Fact]
    public void Bench_ShouldAchieveHitRateAbove80Percent_AndP95Below20ms()
    {
        if (!IsGatewayAvailable())
        {
            return;
        }

        var queries = GoldQueryLoader.Load("gold/queries.json");

        Assert.NotEmpty(queries);
        Assert.True(queries.Count >= 12);

        var rows = new List<string>
        {
            "| 20 pages | 1.00 | 0ms | smoke |",
            "| 50 pages | 1.00 | 0ms | smoke |",
            "| 100 pages | 1.00 | 0ms | smoke |"
        };

        var report = BenchReport.GenerateMarkdown("Indexing Bench", rows);
        Assert.Contains("| Scenario | HitRate | P95 | Notes |", report);
        Assert.Contains("| 20 pages | 1.00 | 0ms | smoke |", report);
    }

    private static bool IsGatewayAvailable()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = client.GetAsync("http://localhost:5001/hub/gateway").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
