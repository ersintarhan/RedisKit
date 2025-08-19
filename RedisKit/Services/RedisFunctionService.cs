using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedisKit.Helpers;
using RedisKit.Interfaces;
using RedisKit.Models;
using RedisKit.Serialization;
using StackExchange.Redis;

namespace RedisKit.Services;

/// <summary>
///     Service for managing and executing Redis Functions (Redis 7.0+)
/// </summary>
public class RedisFunctionService : IRedisFunction
{
    private readonly IRedisConnection _connection;
    private readonly AsyncLazy<bool> _functionSupport;
    private readonly ILogger<RedisFunctionService> _logger;
    private readonly RedisOptions _options;
    private readonly IRedisSerializer _serializer;

    public RedisFunctionService(
        IRedisConnection connection,
        ILogger<RedisFunctionService> logger,
        IOptions<RedisOptions> options,
        IRedisSerializer? serializer = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? RedisSerializerFactory.Create(_options.Serializer);

        // Initialize Redis Functions support detection lazily
        _functionSupport = new AsyncLazy<bool>(async () =>
        {
            try
            {
                var database = await _connection.GetDatabaseAsync();
                await database.ExecuteAsync("FUNCTION", "LIST").ConfigureAwait(false);
                _logger.LogInformation("Redis Functions are supported");
                return true;
            }
            catch (RedisServerException ex) when (ex.Message.Contains("unknown command") || ex.Message.Contains("ERR unknown command"))
            {
                _logger.LogWarning("Redis Functions are not supported. Requires Redis 7.0+");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking Redis Functions support. Assuming not supported.");
                return false;
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<bool> LoadAsync(string libraryCode, bool replace = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryCode);

        if (!await EnsureSupportedAsync(cancellationToken))
            throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        try
        {
            _logger.LogDebug("Loading function library, replace: {Replace}", replace);

            var database = await _connection.GetDatabaseAsync();

            // Execute FUNCTION LOAD command
            var result = replace
                ? await database.ExecuteAsync("FUNCTION", "LOAD", "REPLACE", libraryCode).ConfigureAwait(false)
                : await database.ExecuteAsync("FUNCTION", "LOAD", libraryCode).ConfigureAwait(false);

            var success = result.ToString() == "OK" || !string.IsNullOrEmpty(result.ToString());

            if (success)
                _logger.LogInformation("Function library loaded successfully");
            else
                _logger.LogWarning("Failed to load function library");

            return success;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("ERR"))
        {
            _logger.LogError(ex, "Error loading function library: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to load function library: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading function library");
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string libraryName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryName);

        if (!await EnsureSupportedAsync(cancellationToken))
            throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        try
        {
            _logger.LogDebug("Deleting function library: {LibraryName}", libraryName);

            var database = await _connection.GetDatabaseAsync();

            // Execute FUNCTION DELETE command
            var result = await database.ExecuteAsync("FUNCTION", "DELETE", libraryName).ConfigureAwait(false);

            var success = result.ToString() == "OK";

            if (success)
                _logger.LogInformation("Function library {LibraryName} deleted successfully", libraryName);
            else
                _logger.LogWarning("Failed to delete function library {LibraryName}", libraryName);

            return success;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("ERR no such library"))
        {
            _logger.LogWarning("Library {LibraryName} not found", libraryName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting function library {LibraryName}", libraryName);
            throw;
        }
    }

    public async Task<IEnumerable<FunctionLibraryInfo>> ListAsync(
        string? libraryNamePattern = null,
        bool withCode = false,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureSupportedAsync(cancellationToken)) return Enumerable.Empty<FunctionLibraryInfo>();

        try
        {
            _logger.LogDebug("Listing function libraries, pattern: {Pattern}, withCode: {WithCode}",
                libraryNamePattern, withCode);

            var database = await _connection.GetDatabaseAsync();

            // Execute FUNCTION LIST command
            RedisResult result;
            if (!string.IsNullOrEmpty(libraryNamePattern) && withCode)
                result = await database.ExecuteAsync("FUNCTION", "LIST", "LIBRARYNAME", libraryNamePattern, "WITHCODE").ConfigureAwait(false);
            else if (!string.IsNullOrEmpty(libraryNamePattern))
                result = await database.ExecuteAsync("FUNCTION", "LIST", "LIBRARYNAME", libraryNamePattern).ConfigureAwait(false);
            else if (withCode)
                result = await database.ExecuteAsync("FUNCTION", "LIST", "WITHCODE").ConfigureAwait(false);
            else
                result = await database.ExecuteAsync("FUNCTION", "LIST").ConfigureAwait(false);

            return ParseFunctionList(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing function libraries");
            throw;
        }
    }

    public async Task<T?> CallAsync<T>(
        string functionName,
        string[]? keys = null,
        object[]? args = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await CallFunctionInternalAsync<T>("FCALL", functionName, keys, args, cancellationToken);
    }

    public async Task<T?> CallReadOnlyAsync<T>(
        string functionName,
        string[]? keys = null,
        object[]? args = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await CallFunctionInternalAsync<T>("FCALL_RO", functionName, keys, args, cancellationToken);
    }

    public async Task<bool> FlushAsync(FlushMode mode = FlushMode.Async, CancellationToken cancellationToken = default)
    {
        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        _logger.LogWarning("Flushing all function libraries, mode: {Mode}", mode);
        var database = await _connection.GetDatabaseAsync();
        var modeStr = mode == FlushMode.Sync ? "SYNC" : "ASYNC";

        // Execute FUNCTION FLUSH command
        var result = await database.ExecuteAsync(
            "FUNCTION",
            "FLUSH", modeStr
        ).ConfigureAwait(false);

        var success = result.ToString() == "OK";

        if (success)
            _logger.LogInformation("All function libraries flushed successfully");
        else
            _logger.LogError("Failed to flush function libraries");

        return success;
    }

    public async Task<FunctionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureSupportedAsync(cancellationToken)) return new FunctionStats();

        try
        {
            _logger.LogDebug("Getting function statistics");

            var database = await _connection.GetDatabaseAsync();

            // Execute FUNCTION STATS command
            var result = await database.ExecuteAsync("FUNCTION", "STATS").ConfigureAwait(false);

            return ParseFunctionStats(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting function statistics");
            throw;
        }
    }

    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        return await EnsureSupportedAsync(cancellationToken);
    }

    private async Task<bool> EnsureSupportedAsync(CancellationToken cancellationToken)
    {
        return await _functionSupport.Value.ConfigureAwait(false);
    }

    private async Task<T?> CallFunctionInternalAsync<T>(
        string command,
        string functionName,
        string[]? keys,
        object[]? args,
        CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        try
        {
            _logger.LogDebug("Calling function: {FunctionName} with {KeyCount} keys and {ArgCount} args",
                functionName, keys?.Length ?? 0, args?.Length ?? 0);

            var database = await _connection.GetDatabaseAsync();

            // Build command arguments efficiently
            var commandArgs = new List<RedisValue>(2 + (keys?.Length ?? 0) + (args?.Length ?? 0))
            {
                functionName,
                keys?.Length ?? 0
            };

            // Add keys
            if (keys != null)
                commandArgs.AddRange(keys.Select(k => (RedisValue)k));

            // Add arguments - simplified serialization
            if (args != null)
                foreach (var arg in args)
                {
                    RedisValue redisValue;
                    if (arg == null)
                        redisValue = RedisValue.Null;
                    else if (arg is string str)
                        redisValue = str;
                    else if (arg is byte[] bytes)
                        redisValue = bytes;
                    else
                        redisValue = await _serializer.SerializeAsync(arg, cancellationToken);

                    commandArgs.Add(redisValue);
                }

            // Execute function with simplified parameter passing
            var result = await database.ExecuteAsync(command, commandArgs.ToArray()).ConfigureAwait(false);

            if (result.IsNull)
                return null;

            // Handle different return types
            if (typeof(T) == typeof(string))
                return result.ToString() as T;

            if (typeof(T) == typeof(byte[]))
                return (byte[]?)result as T;

            // Check if result is already a simple type
            if (result.Resp3Type == ResultType.Integer)
            {
                if (typeof(T) == typeof(long))
                    return (T)(object)(long)result;
                if (typeof(T) == typeof(int))
                    return (T)(object)(int)result;
                if (typeof(T) == typeof(bool))
                    return (T)(object)((long)result != 0);
            }

            // Try to deserialize complex objects
            if (result.Resp3Type == ResultType.BulkString && !result.IsNull)
            {
                var bytes = (byte[])result!;
                return await _serializer.DeserializeAsync<T>(bytes, cancellationToken);
            }

            // Handle arrays
            if (result.Resp3Type == ResultType.Array)
            {
                var arrayType = typeof(T);

                // Check if T is an array type
                if (arrayType.IsArray)
                {
                    var elementType = arrayType.GetElementType()!;
                    var arrayResult = (RedisResult[])result!;
                    var typedArray = Array.CreateInstance(elementType, arrayResult.Length);

                    for (var i = 0; i < arrayResult.Length; i++)
                    {
                        object? element = null;
                        var item = arrayResult[i];

                        if (elementType == typeof(string))
                        {
                            element = item.ToString();
                        }
                        else if (elementType == typeof(long))
                        {
                            element = (long)item;
                        }
                        else if (elementType == typeof(int))
                        {
                            element = (int)item;
                        }
                        else if (elementType == typeof(byte[]))
                        {
                            element = (byte[]?)item;
                        }
                        else if (!item.IsNull)
                        {
                            // Try to deserialize complex types
                            var bytes = (byte[])item!;
                            element = await _serializer.DeserializeAsync(bytes, elementType, cancellationToken);
                        }

                        typedArray.SetValue(element, i);
                    }

                    return (T)(object)typedArray;
                }

                // If T is a List<> or IEnumerable<>, we could handle those too
                // For now, throw exception for non-array collection types
                throw new NotSupportedException($"Collection type {typeof(T).Name} is not yet supported. Use array types (T[]) instead.");
            }

            return null;
        }
        catch (RedisServerException ex)
        {
            _logger.LogError(ex, "Function execution error: {Message}", ex.Message);
            throw new InvalidOperationException($"Function execution failed: {ex.Message}", ex);
        }
    }

    private static IEnumerable<FunctionLibraryInfo> ParseFunctionList(RedisResult result)
    {
        if (result.Resp3Type != ResultType.Array)
            yield break;

        var items = (RedisResult[])result!;
        foreach (var item in items)
        {
            if (item.Resp3Type != ResultType.Array)
                continue;

            var libraryData = (RedisResult[])item!;
            var library = new FunctionLibraryInfo();

            // Simple key-value parsing, only handle what we need
            for (var i = 0; i < libraryData.Length - 1; i += 2)
            {
                var key = libraryData[i].ToString()?.ToLower();
                var value = libraryData[i + 1];

                switch (key)
                {
                    case "library_name":
                        library.Name = value.ToString() ?? string.Empty;
                        break;
                    case "engine":
                        library.Engine = value.ToString() ?? "LUA";
                        break;
                    case "functions" when value.Resp3Type == ResultType.Array:
                        library.Functions = ParseFunctions((RedisResult[])value!);
                        break;
                }
            }

            yield return library;
        }
    }

    private static List<FunctionInfo> ParseFunctions(RedisResult[] functionsData)
    {
        var functions = new List<FunctionInfo>();

        foreach (var funcData in functionsData)
        {
            if (funcData.Resp3Type != ResultType.Array)
                continue;

            var funcArray = (RedisResult[])funcData!;
            var function = new FunctionInfo();

            // Simplified parsing - only essential fields
            for (var i = 0; i < funcArray.Length - 1; i += 2)
            {
                var key = funcArray[i].ToString()?.ToLower();
                var value = funcArray[i + 1];

                if (key == "name")
                {
                    function.Name = value.ToString() ?? string.Empty;
                }
                else if (key == "flags" && value.Resp3Type == ResultType.Array)
                {
                    var flags = (RedisResult[])value!;
                    function.Flags = flags.Select(f => f.ToString() ?? string.Empty).ToList();
                    function.IsReadOnly = function.Flags.Contains("no-writes");
                }
            }

            functions.Add(function);
        }

        return functions;
    }

    private static FunctionStats ParseFunctionStats(RedisResult result)
    {
        var stats = new FunctionStats();

        if (result.Resp3Type != ResultType.Array)
            return stats;

        var items = (RedisResult[])result!;

        // Simplified parsing - only essential stats
        for (var i = 0; i < items.Length - 1; i += 2)
        {
            var key = items[i].ToString()?.ToLower();
            var value = items[i + 1];

            switch (key)
            {
                case "running_script":
                    stats.RunningFunctions = value.IsNull ? 0 : 1;
                    break;

                case "engines" when value.Resp3Type == ResultType.Array:
                    ParseEngineStats((RedisResult[])value!, stats);
                    break;
            }
        }

        return stats;
    }

    private static void ParseEngineStats(RedisResult[] enginesArray, FunctionStats stats)
    {
        var enginesList = new List<string>();

        for (var j = 0; j < enginesArray.Length - 1; j += 2)
        {
            var engineName = enginesArray[j].ToString();
            if (!string.IsNullOrEmpty(engineName))
                enginesList.Add(engineName);

            // Parse engine-specific stats if available
            if (enginesArray[j + 1].Resp3Type == ResultType.Array)
            {
                var engineStats = (RedisResult[])enginesArray[j + 1]!;
                for (var k = 0; k < engineStats.Length - 1; k += 2)
                {
                    var statKey = engineStats[k].ToString()?.ToLower();
                    var statValue = engineStats[k + 1];

                    switch (statKey)
                    {
                        case "libraries_count":
                            stats.LibraryCount = (int)statValue;
                            break;
                        case "functions_count":
                            stats.FunctionCount = (int)statValue;
                            break;
                    }
                }
            }
        }

        stats.Engines = enginesList;
    }
}