using System.Text.Json;
using MessagePack;

namespace RedisKit.Models
{
    /// <summary>
    /// Configuration options for Redis connection and operations
    /// </summary>
    public class RedisOptions
    {
        /// <summary>
        /// Connection string for Redis server
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Default TTL for cache entries (if not specified)
        /// </summary>
        public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Prefix for all cache keys
        /// </summary>
        public string CacheKeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Number of retry attempts for failed operations
        /// </summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay between retry attempts
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Timeout for Redis operations
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(5);
        
        /// <summary>
        /// Serializer type to use for serialization
        /// </summary>
        public SerializerType Serializer { get; set; } = SerializerType.MessagePack;
        
        /// <summary>
        /// JSON serializer options (used when Serializer is SystemTextJson)
        /// </summary>
        public JsonSerializerOptions? JsonOptions { get; set; }
        
        /// <summary>
        /// MessagePack serializer options (used when Serializer is MessagePack)
        /// </summary>
        public MessagePackSerializerOptions? MessagePackOptions { get; set; }

        /// <summary>
        /// Advanced connection timeout settings
        /// </summary>
        public ConnectionTimeoutSettings TimeoutSettings { get; set; } = new ConnectionTimeoutSettings();

        /// <summary>
        /// Retry configuration with backoff strategies
        /// </summary>
        public RetryConfiguration RetryConfiguration { get; set; } = new RetryConfiguration();

        /// <summary>
        /// Circuit breaker settings
        /// </summary>
        public CircuitBreakerSettings CircuitBreaker { get; set; } = new CircuitBreakerSettings();

        /// <summary>
        /// Health monitoring settings
        /// </summary>
        public HealthMonitoringSettings HealthMonitoring { get; set; } = new HealthMonitoringSettings();
    }
}
