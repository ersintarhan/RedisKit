namespace RedisKit.Serialization
{
    /// <summary>
    /// Interface for Redis serialization operations supporting both sync and async methods
    /// </summary>
    public interface IRedisSerializer
    {
        /// <summary>
        /// Gets the name of the serializer (e.g., "MessagePack", "SystemTextJson")
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Serializes an object to byte array (synchronous)
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="obj">Object to serialize</param>
        /// <returns>Serialized byte array</returns>
        byte[] Serialize<T>(T obj);

        /// <summary>
        /// Deserializes a byte array to an object (synchronous)
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize</typeparam>
        /// <param name="data">Byte array data to deserialize</param>
        /// <returns>Deserialized object or null if deserialization fails</returns>
        T? Deserialize<T>(byte[] data);

        /// <summary>
        /// Serializes an object to byte array (asynchronous)
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="obj">Object to serialize</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the serialized byte array</returns>
        Task<byte[]> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserializes a byte array to an object (asynchronous)
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize</typeparam>
        /// <param name="data">Byte array data to deserialize</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the deserialized object or null if deserialization fails</returns>
        Task<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Deserializes a byte array to an object using runtime type (asynchronous)
        /// </summary>
        /// <param name="data">Byte array data to deserialize</param>
        /// <param name="type">Target type for deserialization</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the deserialized object or null if deserialization fails</returns>
        Task<object?> DeserializeAsync(byte[] data, Type type, CancellationToken cancellationToken = default);
    }
}