using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    ///     Adds all RedisKit services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure Redis options.</param>
    /// <param name="addHealthCheck">Whether to add a health check for Redis (default: true).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisKit(
        this IServiceCollection services,
        Action<RedisOptions> configureOptions,
        bool addHealthCheck = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        AddRedisKitCore(services);

        if (addHealthCheck)
        {
            services.AddHealthChecks()
                .AddTypeActivatedCheck<RedisHealthCheck>(
                    "redis",
                    HealthStatus.Unhealthy,
                    tags: new[] { "redis", "cache", "database" });
        }

        return services;
    }

    /// <summary>
    ///     Adds all RedisKit services with default configuration (localhost:6379).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisKit(this IServiceCollection services)
    {
        return services.AddRedisKit(options =>
        {
            options.ConnectionString = "localhost:6379";
            options.DefaultTtl = TimeSpan.FromHours(1);
        });
    }

    /// <summary>
    ///     Adds Redis services to the dependency injection container.
    /// </summary>
    [Obsolete("Use AddRedisKit instead. This method will be removed in a future version.")]
    public static IServiceCollection AddRedisServices(
        this IServiceCollection services,
        Action<RedisOptions> configureOptions)
    {
        return services.AddRedisKit(configureOptions);
    }

    /// <summary>
    ///     Adds Redis services with default configuration.
    /// </summary>
    [Obsolete("Use AddRedisKit instead. This method will be removed in a future version.")]
    public static IServiceCollection AddRedisServices(this IServiceCollection services)
    {
        return services.AddRedisKit();
    }

    private static void AddRedisKitCore(IServiceCollection services)
    {
        // Use TryAdd to prevent re-registering services if called multiple times.
        services.TryAddSingleton<IRedisConnection, RedisConnection>();

        // Register the circuit breaker using a factory to ensure it gets the correct settings
        // from the user-configured RedisOptions.
        services.TryAddSingleton<IRedisCircuitBreaker>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<RedisCircuitBreaker>>();
            var redisOptions = provider.GetRequiredService<IOptions<RedisOptions>>();
            var circuitBreakerOptions = Options.Create(redisOptions.Value.CircuitBreaker);
            return new RedisCircuitBreaker(logger, circuitBreakerOptions);
        });

        // Register high-level services. They depend on RedisConnection and are fully async.
        services.TryAddSingleton<IRedisCacheService, RedisCacheService>();
        services.TryAddSingleton<IRedisPubSubService, RedisPubSubService>();
        services.TryAddSingleton<IRedisStreamService, RedisStreamService>();
        services.TryAddSingleton<IDistributedLock, RedisDistributedLock>();
    }
}