namespace Instancium.Vectra.Rag.Prompting;

using System.Text;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Deterministic grounded prompt policy: numbered context chunks, page citations, refusal rule.
/// </summary>
public static class RagPromptBuilder
{
    public const int MaxSnippetChars = 1500;

    public static string Build(string question, IReadOnlyList<RagContextChunk> context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();
        builder.AppendLine("You are a precise technical assistant. Answer strictly from the CONTEXT below.");
        builder.AppendLine("Cite every factual claim with a chunk marker like [1] or [2].");
        builder.AppendLine("If the context does not contain the answer, reply exactly:");
        builder.AppendLine("\"The provided context does not contain enough information.\"");
        builder.AppendLine();
        builder.AppendLine("CONTEXT:");
        if (context.Count == 0)
        {
            builder.AppendLine("(empty)");
        }

        for (int i = 0; i < context.Count; i++)
        {
            RagContextChunk chunk = context[i];
            builder.AppendLine($"[{i + 1}] page {chunk.PageNumber}:");
            builder.AppendLine(Truncate(chunk.Snippet));
        }

        builder.AppendLine();
        builder.AppendLine($"QUESTION: {question}");
        builder.AppendLine();
        builder.AppendLine("ANSWER:");
        return builder.ToString();
    }

    private static string Truncate(string snippet) =>
        string.IsNullOrEmpty(snippet) || snippet.Length <= MaxSnippetChars
            ? snippet
            : snippet[..MaxSnippetChars] + "...";
}
