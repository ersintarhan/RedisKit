namespace RedisKit.Models;

/// <summary>
/// Configuration options for object pooling.
/// </summary>
public class PoolingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether object pooling is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of objects to retain in the pool.
    /// Default is 1024.
    /// </summary>
    public int MaxPoolSize { get; set; } = 1024;
}
