namespace RedisKit.Exceptions;

/// <summary>
///     Exception thrown when a Redis connection error occurs
/// </summary>
public class RedisKitConnectionException : RedisKitException
{
    public RedisKitConnectionException(string message) : base(message)
    {
    }

    public RedisKitConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}