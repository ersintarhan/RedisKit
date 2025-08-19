using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ILogger<RedisFunctionService> _logger;
    private readonly RedisOptions _options;
    private readonly IRedisSerializer _serializer;
    private readonly SemaphoreSlim _supportCheckLock = new(1, 1);
    private bool? _isSupported;

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
    }

    public async Task<bool> LoadAsync(string libraryCode, bool replace = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryCode);

        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        try
        {
            _logger.LogDebug("Loading function library, replace: {Replace}", replace);

            var database = await _connection.GetDatabaseAsync();

            // Execute FUNCTION LOAD command - pass arguments separately
            RedisResult result;
            if (replace)
                result = await database.ExecuteAsync(
                    "FUNCTION",
                    "LOAD", "REPLACE", libraryCode
                ).ConfigureAwait(false);
            else
                result = await database.ExecuteAsync(
                    "FUNCTION",
                    "LOAD", libraryCode
                ).ConfigureAwait(false);

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

        if (!await EnsureSupportedAsync(cancellationToken)) throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        try
        {
            _logger.LogDebug("Deleting function library: {LibraryName}", libraryName);

            var database = await _connection.GetDatabaseAsync();

            // Execute FUNCTION DELETE command
            var result = await database.ExecuteAsync(
                "FUNCTION",
                "DELETE", libraryName
            ).ConfigureAwait(false);

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
        if (_isSupported.HasValue)
            return _isSupported.Value;

        await _supportCheckLock.WaitAsync(cancellationToken);
        try
        {
            if (_isSupported.HasValue)
                return _isSupported.Value;

            var database = await _connection.GetDatabaseAsync();

            try
            {
                // Try to execute FUNCTION LIST to check support
                await database.ExecuteAsync("FUNCTION", "LIST").ConfigureAwait(false);
                _isSupported = true;
                _logger.LogInformation("Redis Functions are supported");
            }
            catch (RedisServerException ex) when (ex.Message.Contains("unknown command") || ex.Message.Contains("ERR unknown command"))
            {
                _isSupported = false;
                _logger.LogWarning("Redis Functions are not supported. Requires Redis 7.0+");
            }
            catch (Exception ex)
            {
                _isSupported = false;
                _logger.LogWarning(ex, "Error checking Redis Functions support. Assuming not supported.");
            }

            return _isSupported.Value;
        }
        finally
        {
            _supportCheckLock.Release();
        }
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

            // Build command arguments
            var commandArgs = new List<RedisValue>
            {
                functionName,
                keys?.Length ?? 0
            };

            // Add keys
            if (keys != null)
                foreach (var key in keys)
                    commandArgs.Add(key);

            // Add arguments
            if (args != null)
                foreach (var arg in args)
                    if (arg == null)
                    {
                        commandArgs.Add(RedisValue.Null);
                    }
                    else if (arg is string str)
                    {
                        commandArgs.Add(str);
                    }
                    else if (arg is byte[] bytes)
                    {
                        commandArgs.Add(bytes);
                    }
                    else
                    {
                        // Serialize complex objects
                        var serialized = await _serializer.SerializeAsync(arg, cancellationToken);
                        commandArgs.Add(serialized);
                    }

            // Execute function - must pass as separate parameters, not as array
            // StackExchange.Redis requires FCALL/FCALL_RO arguments to be passed individually
            // See: https://stackoverflow.com/questions/72863268/how-to-call-redis-functions-using-stackexchange-redis

            // Build the complete command: FCALL functionName numkeys key1 key2... arg1 arg2...
            var fullCommand = new List<RedisValue> { command };
            fullCommand.AddRange(commandArgs);

            // Convert to params array - ExecuteAsync uses params RedisValue[]
            // We need to call it like: ExecuteAsync("FCALL", "functionName", "2", "key1", "key2", "arg1", "arg2")
            // NOT like: ExecuteAsync("FCALL", new[] { "functionName", "2", "key1", "key2", "arg1", "arg2" })

            // Since we can't dynamically create a params call, we need to use reflection or
            // find the overload that takes object[] params
            var paramsArray = commandArgs.Select(v => (object)v).ToArray();
            var result = await database.ExecuteAsync(command, paramsArray).ConfigureAwait(false);

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

    private IEnumerable<FunctionLibraryInfo> ParseFunctionList(RedisResult result)
    {
        var libraries = new List<FunctionLibraryInfo>();

        if (result.Resp3Type != ResultType.Array)
            return libraries;

        var items = (RedisResult[])result!;
        foreach (var item in items)
        {
            if (item.Resp3Type != ResultType.Array)
                continue;

            var libraryData = (RedisResult[])item!;
            var library = new FunctionLibraryInfo();

            for (var i = 0; i < libraryData.Length; i += 2)
            {
                if (i + 1 >= libraryData.Length)
                    break;

                var key = libraryData[i].ToString();
                var value = libraryData[i + 1];

                switch (key?.ToLower())
                {
                    case "library_name":
                        library.Name = value.ToString() ?? string.Empty;
                        break;
                    case "engine":
                        library.Engine = value.ToString() ?? "LUA";
                        break;
                    case "description":
                        library.Description = value.ToString();
                        break;
                    case "library_code":
                        library.Code = value.ToString();
                        break;
                    case "functions":
                        if (value.Resp3Type == ResultType.Array) library.Functions = ParseFunctions((RedisResult[])value!);
                        break;
                }
            }

            libraries.Add(library);
        }

        return libraries;
    }

    private List<FunctionInfo> ParseFunctions(RedisResult[] functionsData)
    {
        var functions = new List<FunctionInfo>();

        foreach (var funcData in functionsData)
        {
            if (funcData.Resp3Type != ResultType.Array)
                continue;

            var funcArray = (RedisResult[])funcData!;
            var function = new FunctionInfo();

            for (var i = 0; i < funcArray.Length; i += 2)
            {
                if (i + 1 >= funcArray.Length)
                    break;

                var key = funcArray[i].ToString();
                var value = funcArray[i + 1];

                switch (key?.ToLower())
                {
                    case "name":
                        function.Name = value.ToString() ?? string.Empty;
                        break;
                    case "description":
                        function.Description = value.ToString();
                        break;
                    case "flags":
                        if (value.Resp3Type == ResultType.Array)
                        {
                            var flags = (RedisResult[])value!;
                            function.Flags = flags.Select(f => f.ToString() ?? string.Empty).ToList();
                            function.IsReadOnly = function.Flags.Contains("no-writes");
                        }

                        break;
                }
            }

            functions.Add(function);
        }

        return functions;
    }

    private FunctionStats ParseFunctionStats(RedisResult result)
    {
        var stats = new FunctionStats();

        if (result.Resp3Type != ResultType.Array)
            return stats;

        var items = (RedisResult[])result!;

        for (var i = 0; i < items.Length; i += 2)
        {
            if (i + 1 >= items.Length)
                break;

            var key = items[i].ToString();
            var value = items[i + 1];

            switch (key?.ToLower())
            {
                case "running_script":
                    stats.RunningFunctions = value.IsNull ? 0 : 1;
                    break;
                case "engines":
                    if (value.Resp3Type == ResultType.Array)
                    {
                        var enginesArray = (RedisResult[])value!;
                        var enginesList = new List<string>();

                        // Parse engines array - format: ["LUA", ["libraries_count", 1, "functions_count", 2]]
                        for (var j = 0; j < enginesArray.Length; j += 2)
                        {
                            if (j >= enginesArray.Length)
                                break;

                            var engineName = enginesArray[j].ToString();
                            if (!string.IsNullOrEmpty(engineName))
                                enginesList.Add(engineName);

                            // Parse engine stats
                            if (j + 1 < enginesArray.Length && enginesArray[j + 1].Resp3Type == ResultType.Array)
                            {
                                var engineStats = (RedisResult[])enginesArray[j + 1]!;
                                for (var k = 0; k < engineStats.Length; k += 2)
                                {
                                    if (k + 1 >= engineStats.Length)
                                        break;

                                    var statKey = engineStats[k].ToString();
                                    var statValue = engineStats[k + 1];

                                    switch (statKey?.ToLower())
                                    {
                                        case "libraries_count":
                                            stats.LibraryCount = (int)statValue;
                                            break;
                                        case "functions_count":
                                            stats.FunctionCount = (int)statValue;
                                            break;
                                        case "memory":
                                            stats.MemoryUsage = (long)statValue;
                                            break;
                                    }
                                }
                            }
                        }

                        stats.Engines = enginesList;
                    }

                    break;
            }
        }

        return stats;
    }
}