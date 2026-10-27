namespace Instancium.Vectra.Tests.Integration.Gold;

using System.Text.Json;
using Instancium.Vectra.Core.Models;

public static class GoldQueryLoader
{
    public static IReadOnlyList<GoldQuery> Load(string relativePath = "gold/queries.json")
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Gold query file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var queries = JsonSerializer.Deserialize<List<GoldQuery>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (queries is null || queries.Count == 0)
        {
            throw new InvalidOperationException("Gold query file is empty.");
        }

        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query.Query))
            {
                throw new InvalidOperationException("Gold query text cannot be empty.");
            }

            if (query.ExpectedPageFrom <= 0 || query.ExpectedPageTo <= 0)
            {
                throw new InvalidOperationException($"Gold query '{query.Query}' has invalid page bounds.");
            }

            if (query.ExpectedPageFrom > query.ExpectedPageTo)
            {
                throw new InvalidOperationException($"Gold query '{query.Query}' has start page greater than end page.");
            }
        }

        return queries;
    }
}
