namespace RedisKit.Serialization;

/// <summary>
///     Interface for Redis serialization operations supporting both synchronous and asynchronous methods.
///     Provides pluggable serialization strategies for converting objects to/from byte arrays for Redis storage.
/// </summary>
/// <remarks>
///     This interface abstracts the serialization mechanism used by RedisKit, allowing you to choose
///     between different serialization formats based on your requirements.
///     Thread Safety: Implementations must be thread-safe and suitable for use as singletons.
///     Available Implementations:
///     - SystemTextJsonRedisSerializer: Uses System.Text.Json for JSON serialization (default)
///     - MessagePackRedisSerializer: Uses MessagePack for binary serialization (faster, smaller)
///     Performance Considerations:
///     - MessagePack: 2-3x faster serialization, 30-50% smaller payload
///     - System.Text.Json: Human-readable, better compatibility, easier debugging
///     Choosing a Serializer:
///     - Use System.Text.Json when:
///     * You need human-readable cache entries
///     * Debugging cache contents directly in Redis
///     * Interoperability with other systems
///     * Working with simple object graphs
///     - Use MessagePack when:
///     * Performance is critical
///     * Network bandwidth is limited
///     * Working with large objects or collections
///     * Binary format is acceptable
///     Custom Implementation Guidelines:
///     - Ensure thread safety for concurrent operations
///     - Handle null inputs gracefully
///     - Implement both sync and async methods efficiently
///     - Consider caching type metadata for better performance
///     - Provide meaningful error messages for serialization failures
///     Usage Example:
///     <code>
/// public class CustomSerializer : IRedisSerializer
/// {
///     public string Name => "Custom";
///     
///     public byte[] Serialize&lt;T&gt;(T obj)
///     {
///         if (obj == null) return Array.Empty&lt;byte&gt;();
///         // Custom serialization logic
///         return MyCustomSerialize(obj);
///     }
///     
///     public T? Deserialize&lt;T&gt;(byte[] data)
///     {
///         if (data == null || data.Length == 0) return default;
///         // Custom deserialization logic
///         return MyCustomDeserialize&lt;T&gt;(data);
///     }
///     
///     // Async methods can delegate to sync for simple cases
///     public Task&lt;byte[]&gt; SerializeAsync&lt;T&gt;(T obj, CancellationToken ct = default)
///         => Task.FromResult(Serialize(obj));
/// }
/// </code>
/// </remarks>
public interface IRedisSerializer
{
    /// <summary>
    ///     Gets the name of the serializer for identification and logging purposes.
    /// </summary>
    /// <value>
    ///     A descriptive name for the serializer (e.g., "MessagePack", "SystemTextJson", "Protobuf").
    ///     This name is used in logs and diagnostics to identify which serializer is being used.
    /// </value>
    /// <remarks>
    ///     The name should be concise and clearly identify the serialization format.
    ///     It's used for debugging and monitoring serialization performance.
    /// </remarks>
    /// <example>
    ///     <code>
    /// var serializer = GetSerializer();
    /// Console.WriteLine($"Using {serializer.Name} for Redis serialization");
    /// // Output: "Using MessagePack for Redis serialization"
    /// </code>
    /// </example>
    string Name { get; }

    /// <summary>
    ///     Serializes an object to a byte array synchronously.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="obj">The object to serialize. Can be null.</param>
    /// <returns>
    ///     A byte array containing the serialized representation of the object.
    ///     Returns an empty array if the input is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the object cannot be serialized (e.g., circular references, unsupported types).
    /// </exception>
    /// <remarks>
    ///     This method should handle null inputs gracefully by returning an empty byte array.
    ///     The serialization should preserve type fidelity for round-trip scenarios.
    ///     For large objects, consider using the async version to avoid blocking.
    /// </remarks>
    /// <example>
    ///     <code>
    /// var user = new User { Id = 1, Name = "John" };
    /// byte[] data = serializer.Serialize(user);
    /// // Store data in Redis
    /// </code>
    /// </example>
    byte[] Serialize<T>(T obj);


    /// <summary>
    ///     Asynchronously serializes an object to a buffer.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <param name="buffer">The buffer to write the serialized data to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of bytes written to the buffer.</returns>
    ValueTask<int> SerializeAsync<T>(T value, Memory<byte> buffer, CancellationToken cancellationToken = default);


    /// <summary>
    ///     Asynchronously serializes an object to a byte array.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="obj">The object to serialize. Can be null.</param>
    /// <param name="cancellationToken">Token to cancel the serialization operation.</param>
    /// <returns>
    ///     A task that represents the asynchronous serialization operation.
    ///     The task result contains the serialized byte array.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the object cannot be serialized.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when the operation is cancelled via the cancellation token.
    /// </exception>
    /// <remarks>
    ///     Use this method for large objects or when operating in async contexts.
    ///     The implementation may use streaming serialization for better memory efficiency.
    ///     Cancellation should be respected to allow for graceful shutdown.
    /// </remarks>
    /// <example>
    ///     <code>
    /// var largeDataset = await LoadDatasetAsync().ConfigureAwait(false);
    /// byte[] data = await serializer.SerializeAsync(largeDataset, cancellationToken).ConfigureAwait(false);
    /// await StoreInRedisAsync("dataset:latest", data).ConfigureAwait(false);
    /// </code>
    /// </example>
    Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default);


    /// <summary>
    ///     Deserializes a byte array to an object synchronously.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize to.</typeparam>
    /// <param name="data">The byte array containing serialized data. Can be null or empty.</param>
    /// <returns>
    ///     The deserialized object, or null/default if the input is null, empty, or deserialization fails.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the data cannot be deserialized to the specified type.
    /// </exception>
    /// <remarks>
    ///     This method should handle null or empty inputs gracefully by returning default(T).
    ///     Type mismatches should be handled with clear error messages.
    ///     The method should be able to deserialize data created by the corresponding Serialize method.
    /// </remarks>
    /// <example>
    ///     <code>
    /// byte[] data = GetFromRedis("user:1");
    /// var user = serializer.Deserialize&lt;User&gt;(data);
    /// if (user != null)
    /// {
    ///     Console.WriteLine($"Retrieved user: {user.Name}");
    /// }
    /// </code>
    /// </example>
    T? Deserialize<T>(byte[] data);


    /// <summary>
    ///     Asynchronously deserializes a byte array to an object.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize to.</typeparam>
    /// <param name="data">The byte array containing serialized data. Can be null or empty.</param>
    /// <param name="cancellationToken">Token to cancel the deserialization operation.</param>
    /// <returns>
    ///     A task that represents the asynchronous deserialization operation.
    ///     The task result contains the deserialized object or null/default.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the data cannot be deserialized to the specified type.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when the operation is cancelled via the cancellation token.
    /// </exception>
    /// <remarks>
    ///     Use this method for large objects or when operating in async contexts.
    ///     The implementation may use streaming deserialization for better memory efficiency.
    ///     Type safety is enforced at compile time through the generic parameter.
    /// </remarks>
    /// <example>
    ///     <code>
    /// byte[] data = await GetFromRedisAsync("dataset:latest").ConfigureAwait(false);
    /// var dataset = await serializer.DeserializeAsync&lt;Dataset&gt;(data, cancellationToken);
    /// if (dataset != null)
    /// {
    ///     await ProcessDatasetAsync(dataset).ConfigureAwait(false);
    /// }
    /// </code>
    /// </example>
    Task<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously deserializes a byte array to an object using runtime type information.
    /// </summary>
    /// <param name="data">The byte array containing serialized data. Can be null or empty.</param>
    /// <param name="type">The target type for deserialization. Must not be null.</param>
    /// <param name="cancellationToken">Token to cancel the deserialization operation.</param>
    /// <returns>
    ///     A task that represents the asynchronous deserialization operation.
    ///     The task result contains the deserialized object or null if deserialization fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the type parameter is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the data cannot be deserialized to the specified type.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when the operation is cancelled via the cancellation token.
    /// </exception>
    /// <remarks>
    ///     This method is useful when the type is not known at compile time.
    ///     It's commonly used in generic frameworks or when working with polymorphic types.
    ///     The returned object needs to be cast to the appropriate type.
    ///     Performance may be slightly slower than the generic version due to runtime type resolution.
    /// </remarks>
    /// <example>
    ///     <code>
    /// // Dynamic type deserialization
    /// Type entityType = GetEntityType(entityName);
    /// byte[] data = await GetFromRedisAsync($"entity:{entityId}").ConfigureAwait(false);
    /// object? entity = await serializer.DeserializeAsync(data, entityType, cancellationToken).ConfigureAwait(false);
    /// 
    /// if (entity != null)
    /// {
    ///     await ProcessEntity(entity, entityType);
    /// }
    /// </code>
    /// </example>
    Task<object?> DeserializeAsync(byte[] data, Type type, CancellationToken cancellationToken = default);


    /// <summary>
    ///     Asynchronously deserializes a memory region to an object.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize to.</typeparam>
    /// <param name="data">The memory region containing serialized data.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deserialized object.</returns>
    ValueTask<T?> DeserializeAsync<T>(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously deserializes a memory region to an object using runtime type information.
    /// </summary>
    /// <param name="data">The memory region containing serialized data.</param>
    /// <param name="type">The target type for deserialization.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deserialized object.</returns>
    ValueTask<object?> DeserializeAsync(ReadOnlyMemory<byte> data, Type type, CancellationToken cancellationToken = default);
}