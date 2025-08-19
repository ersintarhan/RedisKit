namespace RedisKit.Exceptions;

/// <summary>
///     Base exception class for all RedisKit exceptions
/// </summary>
public class RedisKitException : Exception
{
    public RedisKitException(string message) : base(message)
    {
    }

    public RedisKitException(string message, Exception innerException) : base(message, innerException)
    {
    }
}