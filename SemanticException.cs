namespace Console;

public sealed class SemanticException : Exception
{
    public SemanticException(string message)
        : base(message)
    {
    }

    public SemanticException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}