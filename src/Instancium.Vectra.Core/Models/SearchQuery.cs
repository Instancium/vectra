#nullable enable
namespace Instancium.Vectra.Core.Models;

using Instancium.Vectra.Core.Contracts;

/// <summary>
/// A search request against a collection.
/// <see cref="QueryText"/> is the only legal channel for lexical/hybrid rerankers to obtain
/// the original query text; it is passed through to <see cref="IReranker"/> unchanged.
/// </summary>
public sealed record SearchQuery
{
    public SearchQuery(ReadOnlyMemory<float> vector, string? queryText = null, IExpression? filter = null)
    {
        if (vector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be empty.", nameof(vector));
        }

        Vector = vector;
        QueryText = queryText;
        Filter = filter;
    }

    public ReadOnlyMemory<float> Vector { get; }

    public string? QueryText { get; }

    public IExpression? Filter { get; }
}
