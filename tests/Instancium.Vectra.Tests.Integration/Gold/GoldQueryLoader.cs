namespace Instancium.Vectra.Tests.Integration.Gold;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Instancium.Vectra.Core.Models;

public static class GoldQueryLoader
{
    private const string DefaultRelativePath = "gold/queries.json";

    public static IReadOnlyList<GoldQuery> Load(string? path = null)
    {
        string resolved = Path.Combine(AppContext.BaseDirectory, path ?? DefaultRelativePath);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"Gold query set not found at '{resolved}'.", resolved);
        }

        string json = File.ReadAllText(resolved);
        List<GoldQuery>? queries = JsonSerializer.Deserialize<List<GoldQuery>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (queries is null || queries.Count == 0)
        {
            throw new InvalidDataException($"Gold query set at '{resolved}' is empty or malformed.");
        }

        foreach (GoldQuery query in queries)
        {
            if (string.IsNullOrWhiteSpace(query.Query))
            {
                throw new InvalidDataException($"Gold query set at '{resolved}' contains an empty query text.");
            }

            if (query.ExpectedPageFrom <= 0 || query.ExpectedPageTo < query.ExpectedPageFrom)
            {
                throw new InvalidOperationException(
                    $"Gold query '{query.Query}' has invalid page bounds [{query.ExpectedPageFrom}..{query.ExpectedPageTo}].");
            }
        }

        return queries;
    }
}