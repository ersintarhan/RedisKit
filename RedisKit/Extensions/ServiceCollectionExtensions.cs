using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;

namespace RedisKit.Extensions
{
    /// <summary>
    /// Extension methods for configuring Redis services in dependency injection container
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Redis services to the dependency injection container
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

            services.AddSingleton<IRedisPubSubService>(provider => new PubSubService(
                provider.GetRequiredService<ISubscriber>(),
                provider.GetRequiredService<ILogger<PubSubService>>(),
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
        /// Adds Redis services with default configuration
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

            services.AddSingleton<IRedisPubSubService>(provider => new PubSubService(
                provider.GetRequiredService<ISubscriber>(),
                provider.GetRequiredService<ILogger<PubSubService>>(),
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
}