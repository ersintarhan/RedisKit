namespace RedisKit.Models
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
        /// Timeout for configuration retrieval
        /// </summary>
        public TimeSpan ConfigCheckSeconds { get; set; } = TimeSpan.FromSeconds(60);
    }
}