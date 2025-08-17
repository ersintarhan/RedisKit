using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.HealthChecks;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;

namespace RedisKit.Extensions;

/// <summary>
///     Extension methods for configuring Redis services in dependency injection container
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds all RedisKit services to the dependency injection container with a simplified API
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure Redis options</param>
    /// <param name="addHealthCheck">Whether to add health check (default: true)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddRedisKit(
        this IServiceCollection services,
        Action<RedisOptions> configureOptions,
        bool addHealthCheck = true)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        // Register options
        services.Configure(configureOptions);

        // Register Circuit Breaker as a separate service for better testability
        services.AddSingleton<IRedisCircuitBreaker, RedisCircuitBreaker>();

        // Register Redis connection with interface support
        services.AddSingleton<RedisConnection>();

        // Register all other services
        AddRedisServicesInternal(services);

        // Add health check if requested
        if (addHealthCheck)
            services.AddHealthChecks()
                .AddTypeActivatedCheck<RedisHealthCheck>(
                    "redis",
                    HealthStatus.Unhealthy,
                    tags: new[] { "redis", "cache", "database" });

        return services;
    }

    /// <summary>
    ///     Adds all RedisKit services with default configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddRedisKit(this IServiceCollection services)
    {
        return services.AddRedisKit(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.DefaultTtl = TimeSpan.FromHours(1);
            options.CacheKeyPrefix = string.Empty;
            options.RetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromSeconds(1);
            options.OperationTimeout = TimeSpan.FromSeconds(5);
        });
    }

    /// <summary>
    ///     Adds Redis services to the dependency injection container (legacy method for compatibility)
    /// </summary>
    public static IServiceCollection AddRedisServices(
        this IServiceCollection services,
        Action<RedisOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        // Register options
        services.Configure(configureOptions);

        // Register Circuit Breaker
        services.AddSingleton<IRedisCircuitBreaker, RedisCircuitBreaker>();

        // Register Redis connection and database with factory method to ensure proper initialization
        services.AddSingleton<RedisConnection>();

        // Register database and subscriber as separate services to avoid deadlocks
        services.AddSingleton<IDatabaseAsync>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            // This is only called once during startup, not in request context
            return connection.GetDatabaseAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<ISubscriber>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            // This is only called once during startup, not in request context
            return connection.GetSubscriberAsync().GetAwaiter().GetResult();
        });

        // Register services using the pre-initialized database/subscriber
        services.AddSingleton<IRedisCacheService>(provider => new RedisCacheService(
            provider.GetRequiredService<IDatabaseAsync>(),
            provider.GetRequiredService<ILogger<RedisCacheService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        services.AddSingleton<IRedisPubSubService>(provider => new RedisPubSubService(
            provider.GetRequiredService<ISubscriber>(),
            provider.GetRequiredService<ILogger<RedisPubSubService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        // Register StreamService
        services.AddSingleton<IRedisStreamService>(provider => new RedisStreamService(
            provider.GetRequiredService<IDatabaseAsync>(),
            provider.GetRequiredService<ILogger<RedisStreamService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        // Register Distributed Lock Service
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            return connection.GetMultiplexerAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        return services;
    }

    /// <summary>
    ///     Adds Redis services with default configuration (legacy method for compatibility)
    /// </summary>
    public static IServiceCollection AddRedisServices(
        this IServiceCollection services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Register default options
        services.Configure<RedisOptions>(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.DefaultTtl = TimeSpan.FromHours(1);
            options.CacheKeyPrefix = string.Empty;
            options.RetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromSeconds(1);
            options.OperationTimeout = TimeSpan.FromSeconds(5);
        });

        // Register Circuit Breaker
        services.AddSingleton<IRedisCircuitBreaker, RedisCircuitBreaker>();

        // Register Redis connection and database
        services.AddSingleton<RedisConnection>();

        // Register database and subscriber as separate services to avoid deadlocks
        services.AddSingleton<IDatabaseAsync>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            // This is only called once during startup, not in request context
            return connection.GetDatabaseAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<ISubscriber>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            // This is only called once during startup, not in request context
            return connection.GetSubscriberAsync().GetAwaiter().GetResult();
        });

        // Register services using the pre-initialized database/subscriber
        services.AddSingleton<IRedisCacheService>(provider => new RedisCacheService(
            provider.GetRequiredService<IDatabaseAsync>(),
            provider.GetRequiredService<ILogger<RedisCacheService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        services.AddSingleton<IRedisPubSubService>(provider => new RedisPubSubService(
            provider.GetRequiredService<ISubscriber>(),
            provider.GetRequiredService<ILogger<RedisPubSubService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        // Register StreamService
        services.AddSingleton<IRedisStreamService>(provider => new RedisStreamService(
            provider.GetRequiredService<IDatabaseAsync>(),
            provider.GetRequiredService<ILogger<RedisStreamService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        // Register Distributed Lock Service
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            return connection.GetMultiplexerAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        return services;
    }

    /// <summary>
    ///     Internal helper method to register common Redis services
    /// </summary>
    private static IServiceCollection AddRedisServicesInternal(IServiceCollection services)
    {
        // Register database and subscriber as separate services to avoid deadlocks
        services.AddSingleton<IDatabaseAsync>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            // This is only called once during startup, not in request context
            return connection.GetDatabaseAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<ISubscriber>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            // This is only called once during startup, not in request context
            return connection.GetSubscriberAsync().GetAwaiter().GetResult();
        });

        // Register services using the pre-initialized database/subscriber
        services.AddSingleton<IRedisCacheService>(provider => new RedisCacheService(
            provider.GetRequiredService<IDatabaseAsync>(),
            provider.GetRequiredService<ILogger<RedisCacheService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        services.AddSingleton<IRedisPubSubService>(provider => new RedisPubSubService(
            provider.GetRequiredService<ISubscriber>(),
            provider.GetRequiredService<ILogger<RedisPubSubService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        // Register StreamService
        services.AddSingleton<IRedisStreamService>(provider => new RedisStreamService(
            provider.GetRequiredService<IDatabaseAsync>(),
            provider.GetRequiredService<ILogger<RedisStreamService>>(),
            provider.GetRequiredService<IOptions<RedisOptions>>().Value));

        // Register Distributed Lock Service
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var connection = provider.GetRequiredService<RedisConnection>();
            return connection.GetMultiplexerAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        return services;
    }
}