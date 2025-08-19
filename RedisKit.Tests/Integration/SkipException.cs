namespace RedisKit.Tests.Integration;

/// <summary>
///     Exception to skip tests
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message)
    {
    }
}