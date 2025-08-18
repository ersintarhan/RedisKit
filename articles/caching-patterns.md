# Caching Patterns with RedisKit

## Cache-Aside Pattern with ValueTask

The most common caching pattern where the application manages the cache, optimized with ValueTask:

```csharp
public class ProductService
{
    private readonly IRedisCacheService _cache;
    private readonly IProductRepository _repository;
    
    // ValueTask for hot path optimization
    public async ValueTask<Product> GetProductAsync(int productId)
    {
        var key = $"product:{productId}";
        
        // 1. Check cache - ValueTask reduces allocations
        var cached = await _cache.GetAsync<Product>(key);
        if (cached != null)
            return cached;
        
        // 2. Load from database
        var product = await _repository.GetByIdAsync(productId);
        if (product != null)
        {
            // 3. Store in cache - ValueTask for performance
            await _cache.SetAsync(key, product, TimeSpan.FromHours(1));
        }
        
        return product;
    }
    
    public async Task UpdateProductAsync(Product product)
    {
        // Update database
        await _repository.UpdateAsync(product);
        
        // Invalidate cache
        var key = $"product:{product.Id}";
        await _cache.DeleteAsync(key);
    }
}
```

## High-Performance Batch Caching

Use ExecuteBatchAsync for multiple cache operations in a single round-trip:

```csharp
public class BatchCacheService
{
    private readonly IRedisCacheService _cache;
    private readonly IProductRepository _repository;
    
    // Fetch multiple related items in one round-trip
    public async Task<ProductDetails> GetProductDetailsAsync(int productId)
    {
        var result = await _cache.ExecuteBatchAsync(batch =>
        {
            batch.GetAsync<Product>($"product:{productId}");
            batch.GetAsync<List<Review>>($"reviews:{productId}");
            batch.GetAsync<Inventory>($"inventory:{productId}");
            batch.GetAsync<PriceHistory>($"price:history:{productId}");
            batch.ExistsAsync($"featured:{productId}");
        });
        
        return new ProductDetails
        {
            Product = result.GetResult<Product>(0),
            Reviews = result.GetResult<List<Review>>(1) ?? new(),
            Inventory = result.GetResult<Inventory>(2),
            PriceHistory = result.GetResult<PriceHistory>(3),
            IsFeatured = result.GetResult<bool>(4)
        };
    }
    
    // Update multiple cache entries atomically
    public async Task RefreshProductCacheAsync(int productId)
    {
        var product = await _repository.GetProductAsync(productId);
        var reviews = await _repository.GetReviewsAsync(productId);
        var inventory = await _repository.GetInventoryAsync(productId);
        
        await _cache.ExecuteBatchAsync(batch =>
        {
            batch.SetAsync($"product:{productId}", product, TimeSpan.FromHours(1));
            batch.SetAsync($"reviews:{productId}", reviews, TimeSpan.FromMinutes(30));
            batch.SetAsync($"inventory:{productId}", inventory, TimeSpan.FromMinutes(5));
            batch.DeleteAsync($"temp:product:{productId}");
        });
    }
}
```

## Write-Through Pattern

Updates both cache and database simultaneously:

```csharp
public class UserService
{
    private readonly IRedisCacheService _cache;
    private readonly IUserRepository _repository;
    
    public async Task UpdateUserAsync(User user)
    {
        // Update both cache and database
        var key = $"user:{user.Id}";
        
        var tasks = new[]
        {
            _repository.UpdateAsync(user),
            _cache.SetAsync(key, user, TimeSpan.FromHours(24))
        };
        
        await Task.WhenAll(tasks);
    }
}
```

## Cache Warming

Pre-load frequently accessed data:

```csharp
public class CacheWarmer : IHostedService
{
    private readonly IRedisCacheService _cache;
    private readonly IProductRepository _repository;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Load popular products
        var popularProducts = await _repository.GetPopularProductsAsync(100);
        
        var tasks = popularProducts.Select(product =>
            _cache.SetAsync(
                $"product:{product.Id}",
                product,
                TimeSpan.FromHours(24),
                cancellationToken
            )
        );
        
        await Task.WhenAll(tasks);
    }
}
```

## Distributed Locking

Prevent cache stampede with distributed locks using the built-in IDistributedLock interface:

```csharp
public class SafeCacheService
{
    private readonly IRedisCacheService _cache;
    private readonly IDistributedLock _distributedLock;
    
    public SafeCacheService(IRedisCacheService cache, IDistributedLock distributedLock)
    {
        _cache = cache;
        _distributedLock = distributedLock;
    }
    
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiry)
    {
        // Try to get from cache
        var cached = await _cache.GetAsync<T>(key);
        if (cached != null)
            return cached;
        
        // Acquire lock with auto-renewal support
        var lockHandle = await _distributedLock.AcquireLockAsync(
            $"lock:{key}",
            expiry: TimeSpan.FromSeconds(10),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(100)
        );
        
        if (lockHandle != null)
        {
            await using (lockHandle) // Auto-release on dispose
            {
                // Double-check after acquiring lock
                cached = await _cache.GetAsync<T>(key);
                if (cached != null)
                    return cached;
                
                // Generate value
                var value = await factory();
                await _cache.SetAsync(key, value, expiry);
                return value;
            }
        }
        
        // If couldn't acquire lock, wait for unlock
        await _distributedLock.WaitForUnlockAsync($"lock:{key}", TimeSpan.FromSeconds(10));
        return await _cache.GetAsync<T>(key) ?? await factory();
    }
}

// Multi-resource locking example
public class MultiResourceService
{
    private readonly IDistributedLock _distributedLock;
    
    public async Task TransferResourcesAsync(string from, string to, int amount)
    {
        // Acquire locks on multiple resources atomically
        var multiLock = await _distributedLock.AcquireMultiLockAsync(
            new[] { $"account:{from}", $"account:{to}" },
            expiry: TimeSpan.FromSeconds(30)
        );
        
        if (multiLock != null)
        {
            await using (multiLock)
            {
                // Perform atomic transfer operation
                await DebitAccountAsync(from, amount);
                await CreditAccountAsync(to, amount);
            }
        }
        else
        {
            throw new LockAcquisitionException("Could not acquire locks for transfer");
        }
    }
}
```

## Sliding Expiration

Keep frequently accessed items in cache:

```csharp
public class SessionService
{
    private readonly IRedisCacheService _cache;
    
    public async Task<Session> GetSessionAsync(string sessionId)
    {
        var key = $"session:{sessionId}";
        var session = await _cache.GetAsync<Session>(key);
        
        if (session != null)
        {
            // Reset expiration on access
            await _cache.ExpireAsync(key, TimeSpan.FromMinutes(20));
        }
        
        return session;
    }
}
```

## Batch Operations

Optimize multiple cache operations:

```csharp
public class BatchCacheService
{
    private readonly IRedisCacheService _cache;
    
    public async Task<Dictionary<int, Product>> GetProductsAsync(IEnumerable<int> productIds)
    {
        var keys = productIds.Select(id => $"product:{id}").ToArray();
        var results = new Dictionary<int, Product>();
        
        // Batch get
        var cached = await _cache.GetManyAsync<Product>(keys);
        
        var missingIds = new List<int>();
        for (int i = 0; i < productIds.Count(); i++)
        {
            if (cached[i] != null)
            {
                results[productIds.ElementAt(i)] = cached[i];
            }
            else
            {
                missingIds.Add(productIds.ElementAt(i));
            }
        }
        
        // Load missing from database
        if (missingIds.Any())
        {
            var products = await _repository.GetByIdsAsync(missingIds);
            
            // Optimized batch set with Lua script (90-95% faster!)
            var toCache = products.ToDictionary(
                p => $"product:{p.Id}",
                p => p
            );
            
            // Single operation with Lua script optimization
            await _cache.SetManyAsync(toCache, TimeSpan.FromHours(1));
            
            foreach (var product in products)
            {
                results[product.Id] = product;
            }
        }
        
        return results;
    }
}
```

## Cache Invalidation Strategies

### Time-based Invalidation

```csharp
// Fixed expiration
await _cache.SetAsync(key, value, TimeSpan.FromMinutes(30));

// Absolute expiration time
await _cache.SetAsync(key, value, DateTime.UtcNow.AddHours(1));
```

### Event-based Invalidation

```csharp
public class EventBasedCacheInvalidator
{
    private readonly IRedisCacheService _cache;
    private readonly IRedisPubSubService _pubSub;
    
    public async Task InitializeAsync()
    {
        await _pubSub.SubscribeAsync<CacheInvalidationEvent>(
            "cache:invalidation",
            async (channel, evt) =>
            {
                await _cache.DeleteAsync(evt.CacheKey);
            });
    }
    
    public async Task InvalidateAsync(string key)
    {
        await _pubSub.PublishAsync("cache:invalidation", 
            new CacheInvalidationEvent { CacheKey = key });
    }
}
```

### Tag-based Invalidation

```csharp
public class TaggedCacheService
{
    private readonly IRedisCacheService _cache;
    
    public async Task SetWithTagsAsync<T>(string key, T value, 
        TimeSpan expiry, params string[] tags)
    {
        await _cache.SetAsync(key, value, expiry);
        
        // Store key in tag sets
        foreach (var tag in tags)
        {
            await _cache.SetAddAsync($"tag:{tag}", key);
        }
    }
    
    public async Task InvalidateTagAsync(string tag)
    {
        var keys = await _cache.SetMembersAsync<string>($"tag:{tag}");
        
        if (keys?.Any() == true)
        {
            await _cache.DeleteManyAsync(keys.ToArray());
            await _cache.DeleteAsync($"tag:{tag}");
        }
    }
}
```

## High-Performance Batch Pattern with Lua Optimization

```csharp
public class OptimizedBatchService
{
    private readonly IRedisCacheService _cache;
    private readonly ILogger<OptimizedBatchService> _logger;
    
    public async Task BulkImportAsync<T>(Dictionary<string, T> items, TimeSpan ttl)
        where T : class
    {
        // Automatic Lua script optimization for 90-95% performance improvement
        // Single round-trip to Redis instead of O(n) operations
        var result = await _cache.SetManyAsync(items, ttl);
        
        if (result.SuccessCount == items.Count)
        {
            _logger.LogInformation(
                "Successfully cached {Count} items in {ElapsedMs}ms using Lua optimization",
                result.SuccessCount, result.ElapsedMilliseconds);
        }
        else if (result.SuccessCount > 0)
        {
            _logger.LogWarning(
                "Partially cached {Success}/{Total} items. Failed keys: {FailedKeys}",
                result.SuccessCount, items.Count, string.Join(", ", result.FailedKeys));
        }
    }
    
    public async Task SmartBatchSetAsync<T>(IEnumerable<T> items)
        where T : class, IIdentifiable
    {
        // Chunk large datasets for optimal performance
        const int chunkSize = 1000;
        var chunks = items.Chunk(chunkSize);
        
        var tasks = chunks.Select(async chunk =>
        {
            var dict = chunk.ToDictionary(
                item => $"item:{item.Id}",
                item => item
            );
            
            // Each chunk uses Lua script optimization
            return await _cache.SetManyAsync(dict, TimeSpan.FromHours(1));
        });
        
        var results = await Task.WhenAll(tasks);
        
        var totalSuccess = results.Sum(r => r.SuccessCount);
        var totalItems = items.Count();
        
        _logger.LogInformation(
            "Batch import completed: {Success}/{Total} items cached across {Chunks} chunks",
            totalSuccess, totalItems, results.Length);
    }
}

public interface IIdentifiable
{
    string Id { get; }
}
```

## Performance Monitoring Pattern

```csharp
public class MonitoredCacheService
{
    private readonly IRedisCacheService _cache;
    private readonly ILogger<MonitoredCacheService> _logger;
    private readonly IMetrics _metrics;
    
    public async Task<T?> GetWithMonitoringAsync<T>(string key)
        where T : class
    {
        using var activity = Activity.StartActivity("cache.get");
        activity?.SetTag("cache.key", key);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var value = await _cache.GetAsync<T>(key);
            
            stopwatch.Stop();
            
            if (value != null)
            {
                _metrics.RecordCacheHit(key, stopwatch.ElapsedMilliseconds);
                activity?.SetTag("cache.hit", true);
            }
            else
            {
                _metrics.RecordCacheMiss(key, stopwatch.ElapsedMilliseconds);
                activity?.SetTag("cache.hit", false);
            }
            
            // Log slow operations using source-generated logging
            if (stopwatch.ElapsedMilliseconds > 100)
            {
                _logger.LogSlowCacheOperation(key, stopwatch.ElapsedMilliseconds);
            }
            
            return value;
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError(key);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

## Performance Best Practices

1. **Use appropriate expiration times** - Balance between cache hit rate and data freshness
2. **Implement cache stampede protection** - Use built-in IDistributedLock with auto-renewal
3. **Monitor cache hit rates** - Track and optimize based on metrics
4. **Use optimized batch operations** - SetManyAsync with Lua scripts for 90-95% performance gain
5. **Consider compression for large objects** - Trade CPU for network/memory
6. **Implement circuit breakers** - Gracefully handle Redis failures
7. **Use source-generated logging** - Zero-allocation high-performance logging
8. **Leverage parallel serialization** - Utilize all CPU cores for batch operations