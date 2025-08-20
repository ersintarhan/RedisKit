namespace RedisKit.Helpers;

/// <summary>
///     Known Redis server error patterns
/// </summary>
internal static class RedisErrorPatterns
{
    public const string NoScript = "NOSCRIPT";
    public const string BusyGroup = "BUSYGROUP";
    public const string UnknownCommand = "unknown command";
    public const string ErrUnknownCommand = "ERR unknown command";
    public const string NoSuchLibrary = "ERR no such library";
    public const string Loading = "LOADING";
    public const string Err = "ERR";
}