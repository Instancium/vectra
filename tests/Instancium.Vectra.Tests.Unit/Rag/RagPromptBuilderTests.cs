namespace Instancium.Vectra.Tests.Unit.Rag;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Rag.Prompting;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RagPromptBuilderTests
{
    [Fact]
    public void Build_ShouldIncludeNumberedChunksPageCitationsAndQuestion()
    {
        var context = new[]
        {
            new RagContextChunk(1, 12, 0.81f, "First context snippet."),
            new RagContextChunk(2, 33, 0.64f, "Second context snippet.")
        };

        string prompt = RagPromptBuilder.Build("How does a vortex meter work?", context);

        Assert.Contains("[1] page 12:", prompt);
        Assert.Contains("[2] page 33:", prompt);
        Assert.Contains("First context snippet.", prompt);
        Assert.Contains("Second context snippet.", prompt);
        Assert.Contains("QUESTION: How does a vortex meter work?", prompt);
        Assert.Contains("The provided context does not contain enough information.", prompt);
    }

    [Fact]
    public void Build_ShouldTruncateOversizedSnippets()
    {
        string longSnippet = new string('x', RagPromptBuilder.MaxSnippetChars + 500);
        var context = new[] { new RagContextChunk(1, 5, 0.9f, longSnippet) };

        string prompt = RagPromptBuilder.Build("question", context);

        Assert.DoesNotContain(longSnippet, prompt);
        Assert.Contains("...", prompt);
    }

    [Fact]
    public void Build_ShouldRenderEmptyContextMarker()
    {
        string prompt = RagPromptBuilder.Build("question", Array.Empty<RagContextChunk>());

        Assert.Contains("(empty)", prompt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Build_ShouldThrowOnInvalidQuestion(string? question)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => RagPromptBuilder.Build(question!, Array.Empty<RagContextChunk>()));
    }
}
