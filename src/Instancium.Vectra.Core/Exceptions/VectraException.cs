namespace Instancium.Vectra.Core.Exceptions;

public class VectraException : Exception
{
    public VectraException()
    {
    }

    public VectraException(string message)
        : base(message)
    {
    }

    public VectraException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
