using System.Text;

namespace RedisKit.Builders;

/// <summary>
///     Builder for creating Redis Function libraries
/// </summary>
/// <remarks>
///     Provides a fluent API for building Redis Function libraries in Lua or other supported engines.
///     This builder helps create properly formatted function library code that can be loaded into Redis 7.0+.
/// </remarks>
/// <example>
///     <code>
/// var library = new FunctionLibraryBuilder()
///     .WithName("mylib")
///     .WithEngine("LUA")
///     .WithDescription("My function library")
///     .AddFunction("greet", @"
///         function(keys, args)
///             return 'Hello, ' .. args[1]
///         end
///     ")
///     .AddFunction("add", @"
///         function(keys, args)
///             return tonumber(args[1]) + tonumber(args[2])
///         end
///     ")
///     .Build();
/// 
/// await redisFunction.LoadAsync(library);
/// </code>
/// </example>
public class FunctionLibraryBuilder
{
    private string? _libraryName;
    private string _engine = "LUA";
    private string? _description;
    private readonly Dictionary<string, FunctionDefinition> _functions = new();

    /// <summary>
    ///     Sets the library name (required)
    /// </summary>
    /// <param name="name">The name of the library</param>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _libraryName = name;
        return this;
    }

    /// <summary>
    ///     Sets the script engine (default: LUA)
    /// </summary>
    /// <param name="engine">The engine name (e.g., "LUA")</param>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder WithEngine(string engine)
    {
        ArgumentException.ThrowIfNullOrEmpty(engine);
        _engine = engine.ToUpper();
        return this;
    }

    /// <summary>
    ///     Sets the library description (optional)
    /// </summary>
    /// <param name="description">The library description</param>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    /// <summary>
    ///     Adds a function to the library
    /// </summary>
    /// <param name="name">The function name</param>
    /// <param name="implementation">The function implementation code</param>
    /// <param name="flags">Optional flags (e.g., "no-writes" for read-only functions)</param>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder AddFunction(string name, string implementation, params string[] flags)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(implementation);

        _functions[name] = new FunctionDefinition
        {
            Name = name,
            Implementation = implementation.Trim(),
            Flags = flags
        };
        return this;
    }

    /// <summary>
    ///     Adds a read-only function to the library
    /// </summary>
    /// <param name="name">The function name</param>
    /// <param name="implementation">The function implementation code</param>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder AddReadOnlyFunction(string name, string implementation)
    {
        return AddFunction(name, implementation, "no-writes");
    }

    /// <summary>
    ///     Builds the function library code
    /// </summary>
    /// <returns>The complete library code ready to be loaded into Redis</returns>
    /// <exception cref="InvalidOperationException">Thrown when library name is not set or no functions are added</exception>
    public string Build()
    {
        if (string.IsNullOrEmpty(_libraryName))
            throw new InvalidOperationException("Library name is required. Use WithName() to set it.");

        if (_functions.Count == 0)
            throw new InvalidOperationException("At least one function is required. Use AddFunction() to add functions.");

        var sb = new StringBuilder();

        // Add shebang and library declaration
        if (_engine == "LUA")
        {
            sb.AppendLine($"#!lua name={_libraryName}");
            sb.AppendLine();

            // Add description as comment if provided
            if (!string.IsNullOrEmpty(_description))
            {
                sb.AppendLine($"-- {_description}");
                sb.AppendLine();
            }

            // Add each function
            foreach (var function in _functions.Values)
            {
                var flags = "";
                if (function.Flags.Length > 0)
                {
                    var flagList = string.Join(", ", function.Flags.Select(f => $"'{f}'"));
                    flags = $", {{flags = {{{flagList}}}}}";
                }

                sb.AppendLine($"redis.register_function('{function.Name}', {function.Implementation}{flags})");
                sb.AppendLine();
            }
        }
        else
        {
            // For other engines, use a generic format
            // This would need to be adjusted based on the actual engine requirements
            sb.AppendLine($"#!{_engine.ToLower()} name={_libraryName}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(_description))
            {
                sb.AppendLine($"// {_description}");
                sb.AppendLine();
            }

            foreach (var function in _functions.Values)
            {
                sb.AppendLine($"// Function: {function.Name}");
                if (function.Flags.Length > 0)
                {
                    sb.AppendLine($"// Flags: {string.Join(", ", function.Flags)}");
                }
                sb.AppendLine(function.Implementation);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Clears all functions from the builder
    /// </summary>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder Clear()
    {
        _functions.Clear();
        return this;
    }

    /// <summary>
    ///     Resets the builder to its initial state
    /// </summary>
    /// <returns>The builder instance for chaining</returns>
    public FunctionLibraryBuilder Reset()
    {
        _libraryName = null;
        _engine = "LUA";
        _description = null;
        _functions.Clear();
        return this;
    }

    private class FunctionDefinition
    {
        public required string Name { get; init; }
        public required string Implementation { get; init; }
        public string[] Flags { get; init; } = Array.Empty<string>();
    }
}