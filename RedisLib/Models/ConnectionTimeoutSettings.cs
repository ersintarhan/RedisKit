namespace RedisLib.Models
{
    /// <summary>
    /// Connection timeout configuration for Redis operations
    /// </summary>
    public class ConnectionTimeoutSettings
    {
        /// <summary>
        /// Timeout for initial connection establishment
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Timeout for synchronous operations
        /// </summary>
        public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Timeout for asynchronous operations
        /// </summary>
        public TimeSpan AsyncTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Keep-alive interval for connection health checks
        /// </summary>
        public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Response timeout for operations (DEPRECATED)
        /// </summary>
        /// <remarks>
        /// This property is deprecated in StackExchange.Redis 2.7+ and has no effect.
        /// It will be removed in a future version to align with StackExchange.Redis 3.0.
        /// Use SyncTimeout and AsyncTimeout instead for controlling operation timeouts.
        /// </remarks>
        [Obsolete("ResponseTimeout is obsolete in StackExchange.Redis 2.7+ and has no effect. Use SyncTimeout and AsyncTimeout instead.")]
        public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Timeout for configuration retrieval
        /// </summary>
        public TimeSpan ConfigCheckSeconds { get; set; } = TimeSpan.FromSeconds(60);
    }
}