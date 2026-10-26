namespace Instancium.Vectra.Core.Exceptions;

public sealed class IndexCorruptedException : VectraException
{
    public IndexCorruptedException()
    {
    }

    public IndexCorruptedException(string message)
        : base(message)
    {
    }

    public IndexCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
