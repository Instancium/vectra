namespace Instancium.Vectra.Core.Exceptions;

/// <summary>
/// Thrown when no content extractor is registered for a file extension.
/// </summary>
public sealed class UnsupportedFormatException : VectraException
{
    public UnsupportedFormatException()
    {
    }

    public UnsupportedFormatException(string message)
        : base(message)
    {
    }

    public UnsupportedFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
