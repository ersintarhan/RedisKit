# Caching Patterns with RedisKit

## Cache-Aside Pattern

The most common caching pattern where the application manages the cache:

```csharp
public class ProductService
{
    private readonly IRedisCacheService _cache;
    private readonly IProductRepository _repository;
    
    public async Task<Product> GetProductAsync(int productId)
    {
        var key = $"product:{productId}";
        
        // 1. Check cache
        var cached = await _cache.GetAsync<Product>(key);
        if (cached != null)
            return cached;
        
        // 2. Load from database
        var product = await _repository.GetByIdAsync(productId);
        if (product != null)
        {
            // 3. Store in cache
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

Prevent cache stampede with distributed locks:

```csharp
public class SafeCacheService
{
    private readonly IRedisCacheService _cache;
    private readonly IDatabase _redis;
    
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiry)
    {
        // Try to get from cache
        var cached = await _cache.GetAsync<T>(key);
        if (cached != null)
            return cached;
        
        // Acquire lock
        var lockKey = $"lock:{key}";
        var lockToken = Guid.NewGuid().ToString();
        
        if (await _redis.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(10)))
        {
            try
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
            finally
            {
                await _redis.LockReleaseAsync(lockKey, lockToken);
            }
        }
        
        // Wait and retry if couldn't acquire lock
        await Task.Delay(100);
        return await GetOrSetAsync(key, factory, expiry);
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
            
            // Batch set
            var setTasks = products.Select(p =>
                _cache.SetAsync($"product:{p.Id}", p, TimeSpan.FromHours(1))
            );
            await Task.WhenAll(setTasks);
            
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

## Performance Best Practices

1. **Use appropriate expiration times** - Balance between cache hit rate and data freshness
2. **Implement cache stampede protection** - Use locks or probabilistic early expiration
3. **Monitor cache hit rates** - Track and optimize based on metrics
4. **Use pipelining for batch operations** - Reduce network round trips
5. **Consider compression for large objects** - Trade CPU for network/memory
6. **Implement circuit breakers** - Gracefully handle Redis failures