namespace Kbo.Registry;

public sealed class RegistryFormatException : Exception
{
    public RegistryFormatException(string message)
        : base(message)
    {
    }

    public RegistryFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
