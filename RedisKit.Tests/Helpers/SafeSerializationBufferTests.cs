using RedisKit.Helpers;
using Xunit;

namespace RedisKit.Tests.Helpers;

public class SafeSerializationBufferTests
{
    [Fact]
    public void Constructor_ShouldRentBufferFromPool()
    {
        // Arrange & Act
        using var buffer = new SafeSerializationBuffer(512);

        // Assert
        Assert.NotNull(buffer.Buffer);
        Assert.True(buffer.Size >= 512);
        Assert.True(buffer.Memory.Length >= 512);
    }

    [Fact]
    public void Constructor_WithSmallSize_ShouldUseMinimumSize()
    {
        // Arrange & Act
        using var buffer = new SafeSerializationBuffer(10);

        // Assert
        Assert.True(buffer.Size >= 1024); // Minimum size is 1024
    }

    [Fact]
    public void Constructor_WithDefaultSize_ShouldUse1024()
    {
        // Arrange & Act
        using var buffer = new SafeSerializationBuffer();

        // Assert
        Assert.True(buffer.Size >= 1024);
    }

    [Fact]
    public void Buffer_ShouldReturnRentedArray()
    {
        // Arrange
        using var buffer = new SafeSerializationBuffer(2048);

        // Act
        var array = buffer.Buffer;

        // Assert
        Assert.NotNull(array);
        Assert.True(array.Length >= 2048);
    }

    [Fact]
    public void Memory_ShouldReturnMemoryView()
    {
        // Arrange
        using var buffer = new SafeSerializationBuffer(512);

        // Act
        var memory = buffer.Memory;

        // Assert
        Assert.True(memory.Length >= 512);
    }

    [Fact]
    public void Size_ShouldReturnActualBufferSize()
    {
        // Arrange
        using var buffer = new SafeSerializationBuffer(1500);

        // Act
        var size = buffer.Size;

        // Assert
        Assert.True(size >= 1500);
        // ArrayPool may return a larger buffer
        Assert.True(size <= 8192); // Reasonable upper bound
    }

    [Fact]
    public void Dispose_ShouldReturnBufferToPool()
    {
        // Arrange
        var buffer = new SafeSerializationBuffer();
        var arrayRef = buffer.Buffer;

        // Act
        buffer.Dispose();

        // Assert
        // After disposal, accessing properties should throw
        Assert.Throws<ObjectDisposedException>(() => buffer.Buffer);
        Assert.Throws<ObjectDisposedException>(() => buffer.Memory);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var buffer = new SafeSerializationBuffer();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
        {
            buffer.Dispose();
            buffer.Dispose();
            buffer.Dispose();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Buffer_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var buffer = new SafeSerializationBuffer();
        buffer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => buffer.Buffer);
    }

    [Fact]
    public void Memory_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var buffer = new SafeSerializationBuffer();
        buffer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => buffer.Memory);
    }

    [Fact]
    public void Buffer_ShouldBeUsableForWriting()
    {
        // Arrange
        using var buffer = new SafeSerializationBuffer(256);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        data.CopyTo(buffer.Buffer, 0);

        // Assert
        Assert.Equal(1, buffer.Buffer[0]);
        Assert.Equal(2, buffer.Buffer[1]);
        Assert.Equal(3, buffer.Buffer[2]);
        Assert.Equal(4, buffer.Buffer[3]);
        Assert.Equal(5, buffer.Buffer[4]);
    }

    [Fact]
    public void Memory_ShouldBeUsableForWriting()
    {
        // Arrange
        using var buffer = new SafeSerializationBuffer(256);
        var data = new byte[] { 10, 20, 30, 40, 50 };

        // Act
        data.CopyTo(buffer.Memory);

        // Assert
        var span = buffer.Memory.Span;
        Assert.Equal(10, span[0]);
        Assert.Equal(20, span[1]);
        Assert.Equal(30, span[2]);
        Assert.Equal(40, span[3]);
        Assert.Equal(50, span[4]);
    }

    [Fact]
    public void MultipleBuffers_ShouldBeIndependent()
    {
        // Arrange
        using var buffer1 = new SafeSerializationBuffer(512);
        using var buffer2 = new SafeSerializationBuffer(512);

        // Act
        buffer1.Buffer[0] = 100;
        buffer2.Buffer[0] = 200;

        // Assert
        Assert.Equal(100, buffer1.Buffer[0]);
        Assert.Equal(200, buffer2.Buffer[0]);
        Assert.NotSame(buffer1.Buffer, buffer2.Buffer);
    }

    [Fact]
    public async Task Buffer_ShouldBeThreadSafe_ForDisposal()
    {
        // Arrange
        var buffer = new SafeSerializationBuffer();
        var tasks = new Task[10];

        // Act - Multiple threads trying to dispose
        for (var i = 0; i < 10; i++) tasks[i] = Task.Run(() => buffer.Dispose());

        await Task.WhenAll(tasks);

        // Assert - Should not throw
        Assert.Throws<ObjectDisposedException>(() => buffer.Buffer);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void Constructor_WithVariousSizes_ShouldAllocateAppropriately(int requestedSize)
    {
        // Arrange & Act
        using var buffer = new SafeSerializationBuffer(requestedSize);

        // Assert
        Assert.True(buffer.Size >= Math.Max(requestedSize, 1024));
        Assert.NotNull(buffer.Buffer);
        Assert.True(buffer.Memory.Length == buffer.Size);
    }
}