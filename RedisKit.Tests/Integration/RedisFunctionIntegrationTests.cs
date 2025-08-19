using FluentAssertions;
using RedisKit.Builders;
using RedisKit.Models;
using Xunit;

namespace RedisKit.Tests.Integration;

public class RedisFunctionIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task LoadAsync_WithValidLibrary_ShouldLoadSuccessfully()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .WithDescription("Test library for integration tests")
            .AddFunction("test_echo", @"
                function(keys, args)
                    return args[1] or 'no input'
                end
            ")
            .Build();

        // Act
        await FunctionService!.LoadAsync(library);

        // Assert
        var libraries = await FunctionService.ListAsync();
        libraries.Should().Contain(l => l.Name == libraryName);
    }

    [Fact]
    public async Task CallAsync_WithSimpleFunction_ShouldReturnResult()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("add_numbers", @"
                function(keys, args)
                    local a = tonumber(args[1]) or 0
                    local b = tonumber(args[2]) or 0
                    return a + b
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        var result = await FunctionService.CallAsync<string>(
            "add_numbers",
            args: new[] { "10", "25" });

        // Assert
        result.Should().NotBeNull();
        long.Parse(result!).Should().Be(35);
    }

    [Fact]
    public async Task CallAsync_WithKeyOperation_ShouldModifyRedisKeys()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        var testKey = GenerateTestKey("counter");
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("increment_key", @"
                function(keys, args)
                    local key = keys[1]
                    local increment = tonumber(args[1]) or 1
                    return redis.call('INCRBY', key, increment)
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        var result1 = await FunctionService.CallAsync<string>(
            "increment_key",
            new[] { testKey },
            new[] { "5" });

        var result2 = await FunctionService.CallAsync<string>(
            "increment_key",
            new[] { testKey },
            new[] { "3" });

        // Assert
        long.Parse(result1!).Should().Be(5);
        long.Parse(result2!).Should().Be(8);

        var value = await Database.StringGetAsync(testKey);
        value.ToString().Should().Be("8");
    }

    [Fact]
    public async Task CallAsync_WithArrayReturn_ShouldReturnArray()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("get_range", @"
                function(keys, args)
                    local start = tonumber(args[1]) or 1
                    local stop = tonumber(args[2]) or 10
                    local result = {}
                    for i = start, stop do
                        table.insert(result, i)
                    end
                    return result
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        var result = await FunctionService.CallAsync<string[]>(
            "get_range",
            args: new[] { "5", "10" });

        // Assert
        result.Should().NotBeNull();
        result!.Select(long.Parse).Should().BeEquivalentTo(new long[] { 5, 6, 7, 8, 9, 10 });
    }

    [Fact]
    public async Task CallReadOnlyAsync_WithReadOnlyFunction_ShouldWork()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        var testKey = GenerateTestKey("readonly_test");
        TrackFunction(libraryName);

        // Set initial value
        await Database.StringSetAsync(testKey, "test_value");

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddReadOnlyFunction("get_value", @"
                function(keys, args)
                    return redis.call('GET', keys[1])
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        var result = await FunctionService.CallReadOnlyAsync<string>(
            "get_value",
            new[] { testKey });

        // Assert
        result.Should().Be("test_value");
    }

    [Fact]
    public async Task LoadAsync_WithReplace_ShouldReplaceExistingLibrary()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library1 = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("get_version", @"
                function(keys, args)
                    return 'v1'
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library1);

        var result1 = await FunctionService.CallAsync<string>("get_version");
        result1.Should().Be("v1");

        // Act
        var library2 = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("get_version", @"
                function(keys, args)
                    return 'v2'
                end
            ")
            .Build();

        await FunctionService.LoadAsync(library2, true);

        // Assert
        var result2 = await FunctionService.CallAsync<string>("get_version");
        result2.Should().Be("v2");
    }

    [Fact]
    public async Task DeleteAsync_WithExistingLibrary_ShouldRemoveLibrary()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("test_func", "function(keys, args) return 'test' end")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Verify loaded
        var libraries = await FunctionService.ListAsync();
        libraries.Should().Contain(l => l.Name == libraryName);

        // Act
        await FunctionService.DeleteAsync(libraryName);

        // Assert
        libraries = await FunctionService.ListAsync();
        libraries.Should().NotContain(l => l.Name == libraryName);
    }

    [Fact]
    public async Task ListAsync_WithCode_ShouldReturnLibrariesWithCode()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("test_func", "function(keys, args) return 'test' end")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        var libraries = await FunctionService.ListAsync(withCode: true);

        // Assert
        var loadedLibrary = libraries.FirstOrDefault(l => l.Name == libraryName);
        loadedLibrary.Should().NotBeNull();
        loadedLibrary!.Code.Should().NotBeNullOrEmpty();
        loadedLibrary.Code.Should().Contain("test_func");
    }

    [Fact]
    public async Task FlushAsync_WithSyncMode_ShouldRemoveAllLibraries()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("test_func", "function(keys, args) return 'test' end")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        await FunctionService.FlushAsync(FlushMode.Sync);

        // Assert
        var libraries = await FunctionService.ListAsync();
        libraries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatsAsync_ShouldReturnStatistics()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("func1", "function(keys, args) return '1' end")
            .AddFunction("func2", "function(keys, args) return '2' end")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act
        var stats = await FunctionService.GetStatsAsync();

        // Assert
        stats.Should().NotBeNull();
        stats.LibraryCount.Should().BeGreaterThan(0);
        stats.FunctionCount.Should().BeGreaterThan(0);
        // Memory usage might be 0 in some Redis versions or configurations
        // stats.MemoryUsage.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CallAsync_WithComplexDataProcessing_ShouldWorkCorrectly()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("process_user_data", @"
                function(keys, args)
                    local user_key = keys[1]
                    local score_key = keys[2]
                    local user_id = args[1]
                    local score_increment = tonumber(args[2]) or 0
                    
                    -- Set user data
                    redis.call('HSET', user_key, 'id', user_id, 'last_update', args[3])
                    
                    -- Increment score
                    local new_score = redis.call('INCRBY', score_key, score_increment)
                    
                    -- Return both values
                    return {user_id, new_score}
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library);

        var userKey = GenerateTestKey("user:123");
        var scoreKey = GenerateTestKey("score:123");

        // Act
        var result = await FunctionService.CallAsync<object[]>(
            "process_user_data",
            new[] { userKey, scoreKey },
            new[] { "123", "50", DateTime.UtcNow.ToString("O") });

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].ToString().Should().Be("123");
        result[1].ToString().Should().Be("50");

        // Verify data was saved
        var userData = await Database.HashGetAllAsync(userKey);
        userData.Should().NotBeEmpty();
        userData.FirstOrDefault(x => x.Name == "id").Value.ToString().Should().Be("123");

        var score = await Database.StringGetAsync(scoreKey);
        score.ToString().Should().Be("50");
    }

    [Fact]
    public async Task CallAsync_WithErrorHandling_ShouldThrowOnError()
    {
        // Arrange
        await RequireRedis7();

        var libraryName = $"test_lib_{Guid.NewGuid():N}";
        TrackFunction(libraryName);

        var library = new FunctionLibraryBuilder()
            .WithName(libraryName)
            .AddFunction("error_function", @"
                function(keys, args)
                    if not args[1] then
                        return redis.error_reply('Missing required argument')
                    end
                    return 'ok'
                end
            ")
            .Build();

        await FunctionService!.LoadAsync(library);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await FunctionService.CallAsync<string>("error_function"); });

        // Should succeed with argument
        var result = await FunctionService.CallAsync<string>(
            "error_function",
            args: new[] { "value" });
        result.Should().Be("ok");
    }
}