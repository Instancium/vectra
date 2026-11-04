namespace Instancium.Vectra.Core.Exceptions;

/// <summary>
/// Thrown when attempting to add or search a vector whose dimension does not match
/// the dimension fixed in the collection's immutable options.
/// </summary>
public sealed class VectraDimensionMismatchException : VectraException
{
    public VectraDimensionMismatchException(int expected, int actual)
        : base($"Dimension mismatch: collection expects {expected}, but got {actual}.")
    {
        Expected = expected;
        Actual = actual;
    }

    public int Expected { get; }
    public int Actual { get; }
}
