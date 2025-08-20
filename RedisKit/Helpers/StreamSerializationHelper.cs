using StackExchange.Redis;
using RedisKit.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using RedisKit.Serialization;

namespace RedisKit.Helpers;

/// <summary>
/// Helper class for Redis Stream serialization operations
/// </summary>
internal static class StreamSerializationHelper
{
    /// <summary>
    /// Creates a data field for stream entry
    /// </summary>
    public static NameValueEntry CreateDataField<T>(T message, IRedisSerializer serializer)
    {
        var serializedData = serializer.Serialize(message);
        return new NameValueEntry("data", serializedData);
    }

    /// <summary>
    /// Creates data fields for batch messages
    /// </summary>
    public static NameValueEntry[] CreateDataFields<T>(T[] messages, IRedisSerializer serializer)
    {
        var fields = new NameValueEntry[messages.Length];
        for (int i = 0; i < messages.Length; i++)
        {
            fields[i] = CreateDataField(messages[i], serializer);
        }
        return fields;
    }

    /// <summary>
    /// Deserializes stream entry data
    /// </summary>
    public static T? DeserializeStreamEntry<T>(StreamEntry entry, IRedisSerializer serializer) where T : class
    {
        var dataField = GetDataFieldValue(entry);
        if (dataField.HasValue && !dataField.IsNullOrEmpty)
        {
            return serializer.Deserialize<T>(dataField!);
        }
        return default;
    }

    /// <summary>
    /// Deserializes multiple stream entries
    /// </summary>
    public static List<(string id, T data)> DeserializeStreamEntries<T>(
        StreamEntry[] entries, 
        IRedisSerializer serializer) where T : class
    {
        var results = new List<(string id, T data)>(entries.Length);
        
        foreach (var entry in entries)
        {
            var data = DeserializeStreamEntry<T>(entry, serializer);
            if (data != null)
            {
                results.Add((entry.Id.ToString(), data));
            }
        }
        
        return results;
    }

    /// <summary>
    /// Gets the data field value from a stream entry
    /// </summary>
    public static RedisValue GetDataFieldValue(StreamEntry entry)
    {
        foreach (var field in entry.Values)
        {
            if (field.Name == "data")
            {
                return field.Value;
            }
        }
        return RedisValue.Null;
    }

    /// <summary>
    /// Creates a metadata field with serialized metadata
    /// </summary>
    public static NameValueEntry CreateMetadataField<TMetadata>(
        TMetadata metadata, 
        IRedisSerializer serializer)
    {
        var serializedMetadata = serializer.Serialize(metadata);
        return new NameValueEntry("metadata", serializedMetadata);
    }

    /// <summary>
    /// Creates multiple fields for a stream entry with data and metadata
    /// </summary>
    public static NameValueEntry[] CreateFieldsWithMetadata<T, TMetadata>(
        T message, 
        TMetadata metadata,
        IRedisSerializer serializer)
    {
        return new[]
        {
            CreateDataField(message, serializer),
            CreateMetadataField(metadata, serializer)
        };
    }

    /// <summary>
    /// Batch serialize messages for efficient processing
    /// </summary>
    public static async Task<NameValueEntry[][]> BatchSerializeAsync<T>(
        T[] messages,
        IRedisSerializer serializer,
        int batchSize = 100)
    {
        var results = new NameValueEntry[messages.Length][];
        
        // Process in batches to avoid blocking
        for (int i = 0; i < messages.Length; i += batchSize)
        {
            var end = Math.Min(i + batchSize, messages.Length);
            var batchTasks = new Task<NameValueEntry>[end - i];
            
            for (int j = i; j < end; j++)
            {
                var index = j;
                batchTasks[j - i] = Task.Run(() => CreateDataField(messages[index], serializer));
            }
            
            var batchResults = await Task.WhenAll(batchTasks);
            
            for (int j = 0; j < batchResults.Length; j++)
            {
                results[i + j] = new[] { batchResults[j] };
            }
        }
        
        return results;
    }
}