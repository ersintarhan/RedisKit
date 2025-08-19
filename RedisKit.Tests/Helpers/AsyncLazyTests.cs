using RedisKit.Helpers;
using Xunit;

namespace RedisKit.Tests.Helpers;

public class AsyncLazyTests
{
    [Fact]
    public async Task Value_ShouldInitializeOnlyOnce()
    {
        // Arrange
        var initCount = 0;
        var lazy = new AsyncLazy<int>(async () =>
        {
            Interlocked.Increment(ref initCount);
            await Task.Delay(100);
            return 42;
        });

        // Act
        var tasks = new Task<int>[10];
        for (var i = 0; i < 10; i++)
        {
            tasks[i] = lazy.Value;
        }
        
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(1, initCount);
        Assert.All(results, r => Assert.Equal(42, r));
    }

    [Fact]
    public async Task Value_WithThreadSafetyMode_ShouldRespectMode()
    {
        // Arrange
        var lazy = new AsyncLazy<string>(async () =>
        {
            await Task.Delay(50);
            return "test";
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        // Act
        var result = await lazy.Value;

        // Assert
        Assert.Equal("test", result);
        Assert.True(lazy.IsValueCreated);
    }

    [Fact]
    public void IsValueCreated_ShouldReturnFalse_BeforeAccess()
    {
        // Arrange
        var lazy = new AsyncLazy<int>(async () =>
        {
            await Task.Delay(50);
            return 123;
        });

        // Assert
        Assert.False(lazy.IsValueCreated);
    }

    [Fact]
    public async Task IsValueCreated_ShouldReturnTrue_AfterAccess()
    {
        // Arrange
        var lazy = new AsyncLazy<int>(async () =>
        {
            await Task.Delay(50);
            return 123;
        });

        // Act
        _ = await lazy.Value;

        // Assert
        Assert.True(lazy.IsValueCreated);
    }

    [Fact]
    public void GetValueOrDefault_ShouldReturnDefault_WhenNotCreated()
    {
        // Arrange
        var lazy = new AsyncLazy<string?>(async () =>
        {
            await Task.Delay(50);
            return "test";
        });

        // Act
        var result = lazy.GetValueOrDefault();

        // Assert
        Assert.Null(result);
        Assert.False(lazy.IsValueCreated);
    }

    [Fact]
    public async Task GetValueOrDefault_ShouldReturnValue_WhenCreated()
    {
        // Arrange
        var lazy = new AsyncLazy<string>(async () =>
        {
            await Task.Delay(50);
            return "test";
        });

        // Act
        var value = await lazy.Value;
        var result = lazy.GetValueOrDefault();

        // Assert
        Assert.Equal("test", result);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task Value_ShouldPropagateExceptions()
    {
        // Arrange
        var lazy = new AsyncLazy<int>(async () =>
        {
            await Task.Delay(50);
            throw new InvalidOperationException("Test exception");
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await lazy.Value);
        
        // Should throw the same exception on subsequent calls
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await lazy.Value);
    }

    [Fact]
    public async Task Value_WithCancellation_ShouldRespectCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var lazy = new AsyncLazy<int>(async () =>
        {
            await Task.Delay(1000, cts.Token);
            return 42;
        });

        // Act
        var task = lazy.Value;
        cts.Cancel();

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }

    [Fact]
    public async Task GetValueOrDefault_ShouldReturnDefault_WhenTaskFailed()
    {
        // Arrange
        var lazy = new AsyncLazy<string?>(async () =>
        {
            await Task.Delay(50);
            throw new InvalidOperationException("Test");
        });

        // Act
        try
        {
            _ = await lazy.Value;
        }
        catch
        {
            // Ignore the exception
        }

        var result = lazy.GetValueOrDefault();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task MultipleThreads_ShouldInitializeOnlyOnce()
    {
        // Arrange
        var initCount = 0;
        var barrier = new Barrier(5);
        var lazy = new AsyncLazy<int>(async () =>
        {
            var count = Interlocked.Increment(ref initCount);
            await Task.Delay(100);
            return count * 10;
        });

        // Act
        var tasks = new Task<int>[5];
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            tasks[index] = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await lazy.Value;
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(1, initCount);
        Assert.All(results, r => Assert.Equal(10, r));
    }
}