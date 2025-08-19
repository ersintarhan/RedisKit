namespace RedisKit.Exceptions;

/// <summary>
///     Exception thrown when a Redis operation times out
/// </summary>
public class RedisKitTimeoutException : RedisKitException
{
    public RedisKitTimeoutException(string message) : base(message)
    {
    }

    public RedisKitTimeoutException(string message, Exception innerException) : base(message, innerException)
    {
    }
}