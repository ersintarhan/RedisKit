# Redis Functions Guide

Redis Functions is a powerful feature introduced in Redis 7.0 that provides a new way to extend Redis with server-side scripts. It replaces the older EVAL/EVALSHA commands with a more structured and maintainable approach.

## What are Redis Functions?

Redis Functions allow you to:
- Write server-side scripts in Lua
- Organize functions into libraries
- Load and manage function libraries persistently
- Call functions with better performance than EVAL
- Version and manage your server-side logic

## Key Advantages over EVAL/EVALSHA

1. **Persistence**: Functions are stored in Redis and survive restarts
2. **Organization**: Functions are grouped into libraries
3. **Performance**: No need to send script body with each call
4. **Management**: List, delete, and update functions easily
5. **Versioning**: Replace libraries atomically

## Using Redis Functions with RedisKit

### Checking Support

Before using Redis Functions, check if your Redis version supports it:

```csharp
var functionService = services.GetRequiredService<IRedisFunction>();

if (await functionService.IsSupportedAsync())
{
    // Redis 7.0+ is available
}
```

### Creating Function Libraries

Use the `FunctionLibraryBuilder` for easy library creation:

```csharp
using RedisKit.Builders;

var library = new FunctionLibraryBuilder()
    .WithName("myapp")
    .WithEngine("LUA")  // Currently only LUA is supported
    .WithDescription("My application functions")
    .AddFunction("increment_score", @"
        function(keys, args)
            local key = keys[1]
            local increment = tonumber(args[1]) or 1
            return redis.call('INCRBY', key, increment)
        end
    ")
    .AddFunction("get_user_data", @"
        function(keys, args)
            local user_id = args[1]
            local data = {}
            data.score = redis.call('GET', 'user:' .. user_id .. ':score')
            data.level = redis.call('GET', 'user:' .. user_id .. ':level')
            data.name = redis.call('GET', 'user:' .. user_id .. ':name')
            return cjson.encode(data)
        end
    ")
    .AddReadOnlyFunction("count_active_users", @"
        function(keys, args)
            local pattern = 'user:*:active'
            local cursor = '0'
            local count = 0
            repeat
                local result = redis.call('SCAN', cursor, 'MATCH', pattern)
                cursor = result[1]
                count = count + #result[2]
            until cursor == '0'
            return count
        end
    ")
    .Build();
```

### Loading Libraries

```csharp
// Load a new library
await functionService.LoadAsync(library);

// Replace an existing library
await functionService.LoadAsync(library, replace: true);
```

### Calling Functions

```csharp
// Simple function call
var score = await functionService.CallAsync<long>(
    "increment_score", 
    keys: new[] { "user:123:score" },
    args: new[] { "10" });

// Call with complex return type
var userData = await functionService.CallAsync<string>(
    "get_user_data",
    args: new[] { "123" });

// Parse JSON result
var user = JsonSerializer.Deserialize<UserData>(userData);

// Call read-only function (can run on replicas)
var count = await functionService.CallReadOnlyAsync<long>(
    "count_active_users");

// Call with array return type
var results = await functionService.CallAsync<string[]>(
    "get_top_users",
    args: new[] { "10" });
```

### Managing Libraries

```csharp
// List all loaded libraries
var libraries = await functionService.ListAsync();
foreach (var lib in libraries)
{
    Console.WriteLine($"Library: {lib.Name}");
    Console.WriteLine($"Engine: {lib.Engine}");
    Console.WriteLine($"Functions: {lib.Functions.Count}");
    
    foreach (var func in lib.Functions)
    {
        Console.WriteLine($"  - {func.Name} (Read-only: {func.IsReadOnly})");
    }
}

// List with code included
var librariesWithCode = await functionService.ListAsync(withCode: true);

// Delete a library
await functionService.DeleteAsync("myapp");

// Flush all libraries (use with caution!)
await functionService.FlushAsync(FlushMode.Sync);
```

### Getting Statistics

```csharp
var stats = await functionService.GetStatsAsync();
Console.WriteLine($"Libraries: {stats.LibraryCount}");
Console.WriteLine($"Functions: {stats.FunctionCount}");
Console.WriteLine($"Memory Usage: {stats.MemoryUsage} bytes");
Console.WriteLine($"Running Functions: {stats.RunningFunctions}");
```

## Best Practices

### 1. Function Naming

Use descriptive names with a consistent naming convention:

```csharp
.AddFunction("user_get_score", "...")     // Good
.AddFunction("user_update_profile", "...") // Good
.AddFunction("func1", "...")               // Bad
```

### 2. Error Handling

Always handle errors in your Lua functions:

```lua
function(keys, args)
    if #args < 1 then
        return redis.error_reply("Missing required argument")
    end
    
    local value = tonumber(args[1])
    if not value then
        return redis.error_reply("Invalid number format")
    end
    
    -- Your logic here
end
```

### 3. Use Read-Only Functions

Mark functions that don't modify data as read-only:

```csharp
.AddReadOnlyFunction("get_stats", @"
    function(keys, args)
        -- Only reading data, no writes
        return redis.call('GET', keys[1])
    end
")
```

### 4. Atomic Operations

Group related operations in a single function for atomicity:

```csharp
.AddFunction("transfer_points", @"
    function(keys, args)
        local from_user = keys[1]
        local to_user = keys[2]
        local amount = tonumber(args[1])
        
        -- Check balance
        local balance = tonumber(redis.call('GET', from_user)) or 0
        if balance < amount then
            return redis.error_reply('Insufficient balance')
        end
        
        -- Atomic transfer
        redis.call('DECRBY', from_user, amount)
        redis.call('INCRBY', to_user, amount)
        
        return 'OK'
    end
")
```

### 5. Library Versioning

Include version information in your library:

```csharp
var library = new FunctionLibraryBuilder()
    .WithName("myapp_v2")
    .WithDescription("My application functions v2.0.0")
    // ... functions
    .Build();

// Replace old version atomically
await functionService.LoadAsync(library, replace: true);
```

## Common Patterns

### Caching Complex Calculations

```csharp
.AddFunction("calculate_user_rank", @"
    function(keys, args)
        local user_id = args[1]
        local cache_key = 'rank:' .. user_id
        
        -- Check cache
        local cached = redis.call('GET', cache_key)
        if cached then
            return cached
        end
        
        -- Complex calculation
        local score = redis.call('GET', 'user:' .. user_id .. ':score')
        local bonus = redis.call('GET', 'user:' .. user_id .. ':bonus')
        local rank = (tonumber(score) or 0) * 1.5 + (tonumber(bonus) or 0)
        
        -- Cache for 1 hour
        redis.call('SETEX', cache_key, 3600, rank)
        
        return rank
    end
")
```

### Batch Operations

```csharp
.AddFunction("batch_increment", @"
    function(keys, args)
        local results = {}
        for i = 1, #keys do
            local value = redis.call('INCR', keys[i])
            table.insert(results, value)
        end
        return results
    end
")

// Call with multiple keys
var results = await functionService.CallAsync<long[]>(
    "batch_increment",
    keys: new[] { "counter1", "counter2", "counter3" });
```

### Conditional Updates

```csharp
.AddFunction("update_if_greater", @"
    function(keys, args)
        local key = keys[1]
        local new_value = tonumber(args[1])
        
        local current = tonumber(redis.call('GET', key)) or 0
        
        if new_value > current then
            redis.call('SET', key, new_value)
            return 1  -- Updated
        end
        
        return 0  -- Not updated
    end
")
```

## Limitations and Considerations

1. **Lua Only**: Currently, Redis Functions only support Lua as the scripting language
2. **No Async Operations**: Functions run synchronously and can't perform async operations
3. **Memory Usage**: Large libraries consume memory on all Redis nodes
4. **Debugging**: Limited debugging capabilities compared to application code
5. **Cluster Mode**: Functions must be loaded on all cluster nodes

## Migration from EVAL

If you're migrating from EVAL/EVALSHA:

### Before (EVAL)
```csharp
var script = @"
    local value = redis.call('GET', KEYS[1])
    return tonumber(value) or 0
";
var result = await database.ScriptEvaluateAsync(script, new[] { key });
```

### After (Redis Functions)
```csharp
// One-time setup
var library = new FunctionLibraryBuilder()
    .WithName("myapp")
    .AddFunction("get_number", @"
        function(keys, args)
            local value = redis.call('GET', keys[1])
            return tonumber(value) or 0
        end
    ")
    .Build();
await functionService.LoadAsync(library);

// Usage (much more efficient)
var result = await functionService.CallAsync<long>("get_number", keys: new[] { key });
```

## Troubleshooting

### Function Not Found
```csharp
try
{
    await functionService.CallAsync<string>("my_function");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("ERR Function not found"))
{
    // Function doesn't exist - load the library first
}
```

### Library Already Exists
```csharp
try
{
    await functionService.LoadAsync(library);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Library already exists"))
{
    // Use replace: true to update
    await functionService.LoadAsync(library, replace: true);
}
```

### Redis Version Check
```csharp
if (!await functionService.IsSupportedAsync())
{
    // Fall back to EVAL or upgrade Redis
    Console.WriteLine("Redis Functions require Redis 7.0 or later");
}
```

## Next Steps

- [Sharded Pub/Sub Guide](sharded-pubsub.md)
- [Performance Tuning](performance-tuning.md)
- [Advanced Caching Patterns](caching-patterns.md)