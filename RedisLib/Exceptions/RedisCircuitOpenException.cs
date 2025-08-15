namespace RedisLib.Exceptions;

/// <summary>
/// Exception thrown when circuit is open
/// </summary>
public class RedisCircuitOpenException : Exception
{
    public RedisCircuitOpenException() : base("Circuit breaker is open") { }
    public RedisCircuitOpenException(string message) : base(message) { }
    public RedisCircuitOpenException(string message, Exception innerException) : base(message, innerException) { }
}