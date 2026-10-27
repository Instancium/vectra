namespace Instancium.Vectra.Tests.Integration.Bench;

public static class BenchReport
{
    public static string GenerateMarkdown(string title, IReadOnlyList<string> rows)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# " + title);
        builder.AppendLine();
        builder.AppendLine("| Scenario | HitRate | P95 | Notes |");
        builder.AppendLine("| --- | ---: | ---: | --- |");
        foreach (var row in rows)
        {
            builder.AppendLine(row);
        }

        return builder.ToString();
    }
}
