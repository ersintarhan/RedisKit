using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RedisKit.Builders;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Services;
using StackExchange.Redis;
using Xunit;

namespace RedisKit.Tests;

public class RedisFunctionServiceTests
{
    private readonly IRedisConnection _connection;
    private readonly IDatabaseAsync _database;
    private readonly ILogger<RedisFunctionService> _logger;
    private readonly IOptions<RedisOptions> _options;
    private readonly RedisFunctionService _service;

    public RedisFunctionServiceTests()
    {
        _connection = Substitute.For<IRedisConnection>();
        _logger = Substitute.For<ILogger<RedisFunctionService>>();
        _options = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6379",
            Serializer = SerializerType.MessagePack
        });
        _database = Substitute.For<IDatabaseAsync>();

        _connection.GetDatabaseAsync().Returns(Task.FromResult(_database));

        _service = new RedisFunctionService(_connection, _logger, _options);
    }

    [Fact]
    public async Task LoadAsync_Should_Load_Function_Library()
    {
        // Arrange
        var libraryCode = "#!lua name=mylib\nredis.register_function('test', function() return 'hello' end)";
        var mockDatabase = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        _connection.GetDatabaseAsync().Returns(Task.FromResult((IDatabaseAsync)mockDatabase));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDatabase);

        // Simulate Redis 7.0+ support
        mockDatabase.ExecuteAsync("FUNCTION", "LIST")
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue[] { })));

        mockDatabase.ExecuteAsync("FUNCTION", Arg.Any<object[]>())
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue("mylib"))));

        // Act
        var result = await _service.LoadAsync(libraryCode);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_Should_Delete_Function_Library()
    {
        // Arrange
        var libraryName = "mylib";
        var mockDatabase = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        _connection.GetDatabaseAsync().Returns(Task.FromResult((IDatabaseAsync)mockDatabase));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDatabase);

        // Simulate Redis 7.0+ support
        mockDatabase.ExecuteAsync("FUNCTION", "LIST")
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue[] { })));

        mockDatabase.ExecuteAsync("FUNCTION", Arg.Any<object[]>())
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue("OK"))));

        // Act
        var result = await _service.DeleteAsync(libraryName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CallAsync_Should_Execute_Function()
    {
        // Arrange
        var functionName = "mylib.test";
        var expectedResult = "hello world";
        var mockDatabase = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        _connection.GetDatabaseAsync().Returns(Task.FromResult((IDatabaseAsync)mockDatabase));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDatabase);

        // Simulate Redis 7.0+ support
        mockDatabase.ExecuteAsync("FUNCTION", "LIST")
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue[] { })));

        mockDatabase.ExecuteAsync("FCALL", Arg.Any<object[]>())
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue(expectedResult))));

        // Act
        var result = await _service.CallAsync<string>(functionName);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task IsSupportedAsync_Should_Return_False_For_Older_Redis()
    {
        // Arrange
        var mockDatabase = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        _connection.GetDatabaseAsync().Returns(Task.FromResult((IDatabaseAsync)mockDatabase));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDatabase);

        // Simulate older Redis (< 7.0)
        mockDatabase.ExecuteAsync("FUNCTION", "LIST")
            .Returns<RedisResult>(_ => throw new RedisServerException("ERR unknown command 'FUNCTION'"));

        // Act
        var result = await _service.IsSupportedAsync();

        // Assert  
        Assert.False(result);

        // Second call should use cached result
        var result2 = await _service.IsSupportedAsync();
        Assert.False(result2);
    }

    [Fact]
    public void FunctionLibraryBuilder_Should_Build_Lua_Library()
    {
        // Arrange & Act
        var library = new FunctionLibraryBuilder()
            .WithName("testlib")
            .WithEngine("LUA")
            .WithDescription("Test library")
            .AddFunction("hello", @"
                function(keys, args)
                    return 'Hello, ' .. args[1]
                end
            ")
            .Build();

        // Assert
        Assert.Contains("#!lua name=testlib", library);
        Assert.Contains("redis.register_function('hello'", library);
    }

    [Fact]
    public void FunctionLibraryBuilder_Should_Throw_Without_Name()
    {
        // Arrange
        var builder = new FunctionLibraryBuilder()
            .AddFunction("test", "function() return 1 end");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void FunctionLibraryBuilder_Should_Throw_Without_Functions()
    {
        // Arrange
        var builder = new FunctionLibraryBuilder()
            .WithName("testlib");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public async Task FlushAsync_Should_Flush_All_Functions()
    {
        // Arrange
        var mockDatabase = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        _connection.GetDatabaseAsync().Returns(Task.FromResult((IDatabaseAsync)mockDatabase));
        _connection.GetMultiplexerAsync().Returns(Task.FromResult(multiplexer));
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDatabase);

        // Simulate Redis 7.0+ support
        mockDatabase.ExecuteAsync("FUNCTION", "LIST")
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue[] { })));

        mockDatabase.ExecuteAsync("FUNCTION", Arg.Any<object[]>())
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue("OK"))));

        // Act
        var result = await _service.FlushAsync();

        // Assert
        Assert.True(result);
    }
}