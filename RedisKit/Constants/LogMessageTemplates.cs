namespace RedisKit.Constants;

/// <summary>
/// Contains log message templates to avoid string literal duplication
/// </summary>
internal static class LogMessageTemplates
{
    // Redis operation error templates
    public const string RedisTimeout = "Redis timeout during {Operation} for key: {Key}";
    public const string UnexpectedError = "Unexpected error during {Operation} for key: {Key}";
    public const string ConnectionError = "Redis connection error during {Operation} for key: {Key}";
    public const string ServerError = "Redis server error during {Operation} for key: {Key}";
    public const string OperationRetry = "Retrying {Operation} for key: {Key}, attempt {Attempt}";
    public const string CircuitBreakerOpen = "Circuit breaker is open for {Operation}";
    
    // Cache operation templates
    public const string CacheHit = "Cache hit for key: {Key}";
    public const string CacheMiss = "Cache miss for key: {Key}";
    public const string CacheExpired = "Cache entry expired for key: {Key}";
    
    // Serialization templates
    public const string SerializationError = "Serialization error for type {Type}: {Error}";
    public const string DeserializationError = "Deserialization error for type {Type}: {Error}";
    
    // Lock operation templates
    public const string LockAcquired = "Lock acquired for resource: {Resource}";
    public const string LockReleased = "Lock released for resource: {Resource}";
    public const string LockTimeout = "Lock acquisition timeout for resource: {Resource}";
    
    // Stream operation templates
    public const string StreamMessage = "Processing stream message {MessageId} from stream {StreamName}";
    public const string ConsumerGroupCreated = "Consumer group {GroupName} created for stream {StreamName}";
    
    // PubSub operation templates
    public const string MessagePublished = "Message published to channel {Channel}";
    public const string SubscriptionCreated = "Subscription created for pattern {Pattern}";
}