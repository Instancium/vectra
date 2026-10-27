#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record GoldQuery
{
    public string Query { get; init; } = string.Empty;

    public int ExpectedPageFrom { get; init; }

    public int ExpectedPageTo { get; init; }
}
