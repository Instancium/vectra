namespace Instancium.Vectra.Core.Contracts;

/// <summary>
/// Marker for metadata filter expressions (reserved by TZ §5.1, evaluator lands later).
/// Until an evaluator exists, search kernels must fail fast with
/// System.NotSupportedException when SearchQuery.Filter is not null.
/// Silently ignoring a filter is forbidden.
/// </summary>
public interface IExpression
{
}
