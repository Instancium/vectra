namespace Instancium.Vectra.Core.Exceptions;

public sealed class IndexingFailedException : VectraException
{
    public IndexingFailedException()
    {
    }

    public IndexingFailedException(string message)
        : base(message)
    {
    }

    public IndexingFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
