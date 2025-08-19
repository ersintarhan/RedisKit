namespace RedisKit.Exceptions;

/// <summary>
///     Exception thrown when a Redis server error occurs
/// </summary>
public class RedisKitServerException : RedisKitException
{
    public RedisKitServerException(string message) : base(message)
    {
    }

    public RedisKitServerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}