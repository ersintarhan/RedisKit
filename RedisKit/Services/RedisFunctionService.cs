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
    private const string FunctionCommand = "FUNCTION";
    private const string NotSupportedMessage = "Redis Functions are not supported. Requires Redis 7.0+";

    private readonly IRedisConnection _connection;
    private readonly AsyncLazy<bool> _functionSupport;
    private readonly ILogger<RedisFunctionService> _logger;
    private readonly IRedisSerializer _serializer;


    public RedisFunctionService(
        IRedisConnection connection,
        ILogger<RedisFunctionService> logger,
        IOptions<RedisOptions> options,
        IRedisSerializer? serializer = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var redisOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? RedisSerializerFactory.Create(redisOptions.Serializer);

        // Initialize Redis Functions support detection lazily
        _functionSupport = new AsyncLazy<bool>(async () =>
        {
            var supportResult = await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync(
                async () =>
                {
                    var database = await _connection.GetDatabaseAsync();
                    await database.ExecuteAsync(FunctionCommand, "LIST").ConfigureAwait(false);
                    _logger.LogInformation("Redis Functions are supported");
                    return (object?)true;
                },
                _logger,
                RedisErrorPatterns.UnknownCommand,
                false,
                operationName: "FunctionSupportCheck"
            ).ConfigureAwait(false);

            return supportResult is bool result && result;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<bool> LoadAsync(string libraryCode, bool replace = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryCode);

        if (!await EnsureSupportedAsync())
            throw new NotSupportedException(NotSupportedMessage);

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                _logger.LogDebug("Loading function library, replace: {Replace}", replace);

                var database = await _connection.GetDatabaseAsync();

                // Execute FUNCTION LOAD command
                var result = replace
                    ? await database.ExecuteAsync(FunctionCommand, "LOAD", "REPLACE", libraryCode).ConfigureAwait(false)
                    : await database.ExecuteAsync(FunctionCommand, "LOAD", libraryCode).ConfigureAwait(false);

                var success = result.ToString() == "OK" || !string.IsNullOrEmpty(result.ToString());

                if (success)
                    _logger.LogInformation("Function library loaded successfully");
                else
                    _logger.LogWarning("Failed to load function library");

                return (object?)success;
            },
            _logger,
            handleSpecificExceptions: ex =>
            {
                if (ex is RedisServerException rse && rse.Message.Contains(RedisErrorPatterns.Err))
                {
                    _logger.LogError(ex, "Error loading function library: {Message}", ex.Message);
                    throw new InvalidOperationException($"Failed to load function library: {ex.Message}", ex);
                }

                return null;
            }
        ).ConfigureAwait(false) is true;
    }

    public async Task<bool> DeleteAsync(string libraryName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryName);

        if (!await EnsureSupportedAsync())
            throw new NotSupportedException(NotSupportedMessage);

        return await RedisOperationExecutor.ExecuteWithSilentErrorHandlingAsync(
            async () =>
            {
                _logger.LogDebug("Deleting function library: {LibraryName}", libraryName);

                var database = await _connection.GetDatabaseAsync();

                // Execute FUNCTION DELETE command
                var result = await database.ExecuteAsync(FunctionCommand, "DELETE", libraryName).ConfigureAwait(false);

                var success = result.ToString() == "OK";

                if (success)
                    _logger.LogInformation("Function library {LibraryName} deleted successfully", libraryName);
                else
                    _logger.LogWarning("Failed to delete function library {LibraryName}", libraryName);

                return (object?)success;
            },
            _logger,
            RedisErrorPatterns.NoSuchLibrary,
            false
        ).ConfigureAwait(false) is true;
    }

    public async Task<IEnumerable<FunctionLibraryInfo>> ListAsync(
        string? libraryNamePattern = null,
        bool withCode = false,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureSupportedAsync()) return [];

        _logger.LogDebug("Listing function libraries, pattern: {Pattern}, withCode: {WithCode}",
            libraryNamePattern, withCode);

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                var database = await _connection.GetDatabaseAsync();

                // Execute FUNCTION LIST command
                RedisResult result;
                if (!string.IsNullOrEmpty(libraryNamePattern) && withCode)
                    result = await database.ExecuteAsync(FunctionCommand, "LIST", "LIBRARYNAME", libraryNamePattern, "WITHCODE").ConfigureAwait(false);
                else if (!string.IsNullOrEmpty(libraryNamePattern))
                    result = await database.ExecuteAsync(FunctionCommand, "LIST", "LIBRARYNAME", libraryNamePattern).ConfigureAwait(false);
                else if (withCode)
                    result = await database.ExecuteAsync(FunctionCommand, "LIST", "WITHCODE").ConfigureAwait(false);
                else
                    result = await database.ExecuteAsync(FunctionCommand, "LIST").ConfigureAwait(false);

                return ParseFunctionList(result);
            },
            _logger,
            libraryNamePattern,
            cancellationToken
        ).ConfigureAwait(false) ?? [];
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
        if (!await EnsureSupportedAsync()) throw new NotSupportedException("Redis Functions are not supported. Requires Redis 7.0+");

        _logger.LogWarning("Flushing all function libraries, mode: {Mode}", mode);
        var database = await _connection.GetDatabaseAsync();
        var modeStr = mode == FlushMode.Sync ? "SYNC" : "ASYNC";

        // Execute FUNCTION FLUSH command
        var result = await database.ExecuteAsync(
            FunctionCommand,
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
        if (!await EnsureSupportedAsync()) return new FunctionStats();

        _logger.LogDebug("Getting function statistics");

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                var database = await _connection.GetDatabaseAsync();

                // Execute FUNCTION STATS command
                var result = await database.ExecuteAsync(FunctionCommand, "STATS").ConfigureAwait(false);

                return ParseFunctionStats(result);
            },
            _logger,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false) ?? new FunctionStats();
    }

    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        return await EnsureSupportedAsync();
    }

    private async Task<bool> EnsureSupportedAsync()
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

        if (!await EnsureSupportedAsync())
            throw new NotSupportedException(NotSupportedMessage);

        return await RedisOperationExecutor.ExecuteAsync(
            async () =>
            {
                var database = await _connection.GetDatabaseAsync();
                var commandArgs = BuildCommandArguments(functionName, keys);
                await PrepareArgumentsAsync(commandArgs, args, cancellationToken);

                var result = await ExecuteFunctionAsync(database, command, commandArgs);
                return await ConvertResultAsync<T>(result, cancellationToken);
            },
            _logger,
            functionName,
            cancellationToken,
            ex =>
            {
                if (ex is RedisServerException redisEx)
                {
                    _logger.LogError(redisEx, "Function execution error: {Message}", redisEx.Message);
                    throw new InvalidOperationException($"Function execution failed: {redisEx.Message}", redisEx);
                }

                return null; // Let RedisOperationExecutor handle other exceptions
            }
        ).ConfigureAwait(false);
    }

    private static List<object> BuildCommandArguments(string functionName, string[]? keys)
    {
        // Build command arguments for FCALL/FCALL_RO
        // Format: FCALL function_name numkeys [keys...] [args...]
        var commandArgs = new List<object>
        {
            functionName,
            keys?.Length ?? 0
        };

        // Add keys
        if (keys != null)
            commandArgs.AddRange(keys);

        return commandArgs;
    }

    private async Task PrepareArgumentsAsync(List<object> commandArgs, object[]? args, CancellationToken cancellationToken)
    {
        if (args == null) return;

        foreach (var arg in args)
        {
            var preparedArg = await PrepareArgumentAsync(arg, cancellationToken);
            commandArgs.Add(preparedArg);
        }
    }

    private async Task<object> PrepareArgumentAsync(object? arg, CancellationToken cancellationToken)
    {
        if (arg == null)
            return RedisValue.Null;

        if (arg is string || arg is int || arg is long || arg is double || arg is bool)
            return arg; // Pass primitive types directly

        if (arg is byte[] bytes)
            return bytes;

        return await _serializer.SerializeAsync(arg, cancellationToken);
    }

    private static async Task<RedisResult> ExecuteFunctionAsync(IDatabaseAsync database, string command, List<object> commandArgs)
    {
        // StackExchange.Redis requires parameters as individual arguments
        return commandArgs.Count switch
        {
            2 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1]).ConfigureAwait(false),
            3 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1], commandArgs[2]).ConfigureAwait(false),
            4 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1], commandArgs[2], commandArgs[3]).ConfigureAwait(false),
            5 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1], commandArgs[2], commandArgs[3], commandArgs[4]).ConfigureAwait(false),
            6 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1], commandArgs[2], commandArgs[3], commandArgs[4], commandArgs[5]).ConfigureAwait(false),
            7 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1], commandArgs[2], commandArgs[3], commandArgs[4], commandArgs[5], commandArgs[6]).ConfigureAwait(false),
            8 => await database.ExecuteAsync(command, commandArgs[0], commandArgs[1], commandArgs[2], commandArgs[3], commandArgs[4], commandArgs[5], commandArgs[6], commandArgs[7]).ConfigureAwait(false),
            _ => throw new ArgumentException($"Too many arguments for FCALL. Maximum supported is 6 keys/args combined, got {commandArgs.Count - 2}")
        };
    }

    private async Task<T?> ConvertResultAsync<T>(RedisResult result, CancellationToken cancellationToken) where T : class
    {
        if (result.IsNull)
            return null;

        // Handle simple string type
        if (typeof(T) == typeof(string))
            return result.ToString() as T;

        // Handle byte array
        if (typeof(T) == typeof(byte[]))
            return (byte[]?)result as T;

        // Handle integer types
        if (result.Resp3Type == ResultType.Integer)
            return ConvertIntegerResult<T>(result);

        // Handle bulk string (complex objects)
        if (result.Resp3Type == ResultType.BulkString && !result.IsNull)
        {
            var bytes = (byte[])result!;
            return await _serializer.DeserializeAsync<T>(bytes, cancellationToken);
        }

        // Handle array results
        if (result.Resp3Type == ResultType.Array && typeof(T).IsArray)
            return await ConvertArrayResultAsync<T>(result, cancellationToken);

        if (result.Resp3Type == ResultType.Array)
            throw new NotSupportedException($"Collection type {typeof(T).Name} is not yet supported. Use array types (T[]) instead.");

        return null;
    }

    private static T? ConvertIntegerResult<T>(RedisResult result) where T : class
    {
        if (typeof(T) == typeof(long))
            return (T)(object)(long)result;
        if (typeof(T) == typeof(int))
            return (T)(object)(int)result;
        if (typeof(T) == typeof(bool))
            return (T)(object)((long)result != 0);
        return null;
    }

    private async Task<T?> ConvertArrayResultAsync<T>(RedisResult result, CancellationToken cancellationToken) where T : class
    {
        var arrayType = typeof(T);
        if (!arrayType.IsArray)
            return null;

        var elementType = arrayType.GetElementType()!;
        var arrayResult = (RedisResult[])result!;
        var typedArray = Array.CreateInstance(elementType, arrayResult.Length);

        for (var i = 0; i < arrayResult.Length; i++)
        {
            var element = await ConvertArrayElementAsync(arrayResult[i], elementType, cancellationToken);
            typedArray.SetValue(element, i);
        }

        return (T)(object)typedArray;
    }

    private async Task<object?> ConvertArrayElementAsync(RedisResult item, Type elementType, CancellationToken cancellationToken)
    {
        if (elementType == typeof(string))
            return item.ToString();

        if (elementType == typeof(long))
            return (long)item;

        if (elementType == typeof(int))
            return (int)item;

        if (elementType == typeof(byte[]))
            return (byte[]?)item;

        if (!item.IsNull)
        {
            var bytes = (byte[])item!;
            return await _serializer.DeserializeAsync(bytes, elementType, cancellationToken);
        }

        return null;
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

            var library = ParseSingleLibrary((RedisResult[])item!);
            yield return library;
        }
    }

    private static FunctionLibraryInfo ParseSingleLibrary(RedisResult[] libraryData)
    {
        var library = new FunctionLibraryInfo();

        // Simple key-value parsing, only handle what we need
        for (var i = 0; i < libraryData.Length - 1; i += 2)
        {
            var key = libraryData[i].ToString().ToLower();
            var value = libraryData[i + 1];

            SetLibraryProperty(library, key, value);
        }

        return library;
    }

    private static void SetLibraryProperty(FunctionLibraryInfo library, string? key, RedisResult value)
    {
        switch (key)
        {
            case "library_name":
                library.Name = value.ToString();
                break;
            case "engine":
                library.Engine = value.ToString();
                break;
            case "description":
                library.Description = value.IsNull ? null : value.ToString();
                break;
            case "library_code":
                // Set code if value is not null
                if (!value.IsNull) library.Code = value.ToString();

                break;
            case "functions" when value.Resp3Type == ResultType.Array:
                library.Functions = ParseFunctions((RedisResult[])value!);
                break;
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
                var key = funcArray[i].ToString().ToLower();
                var value = funcArray[i + 1];

                if (key == "name")
                {
                    function.Name = value.ToString();
                }
                else if (key == "flags" && value.Resp3Type == ResultType.Array)
                {
                    var flags = (RedisResult[])value!;
                    function.Flags = flags.Select(f => f.ToString()).ToList();
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
                    var statKey = engineStats[k].ToString().ToLower();
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