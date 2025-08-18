using System.Text;

namespace RedisKit.Models;

/// <summary>
///     Builder for creating Redis function libraries
/// </summary>
public class FunctionLibraryBuilder
{
    private readonly List<(string name, string code, bool isReadOnly)> _functions = new();
    private string? _description;
    private string _engine = "LUA";
    private string _name = string.Empty;

    /// <summary>
    ///     Set the library name
    /// </summary>
    public FunctionLibraryBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    ///     Set the scripting engine
    /// </summary>
    public FunctionLibraryBuilder WithEngine(string engine)
    {
        _engine = engine.ToUpper();
        return this;
    }

    /// <summary>
    ///     Set the library description
    /// </summary>
    public FunctionLibraryBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>
    ///     Add a function to the library
    /// </summary>
    public FunctionLibraryBuilder AddFunction(string name, string code, bool isReadOnly = false)
    {
        _functions.Add((name, code, isReadOnly));
        return this;
    }

    /// <summary>
    ///     Build the function library code
    /// </summary>
    public string Build()
    {
        if (string.IsNullOrEmpty(_name))
            throw new InvalidOperationException("Library name is required");

        if (_functions.Count == 0)
            throw new InvalidOperationException("At least one function is required");

        if (_engine == "LUA") return BuildLuaLibrary();

        if (_engine == "JS") return BuildJavaScriptLibrary();

        throw new NotSupportedException($"Engine '{_engine}' is not supported");
    }

    private string BuildLuaLibrary()
    {
        var code = new StringBuilder();

        // Library header
        code.AppendLine($"#!lua name={_name}");

        if (!string.IsNullOrEmpty(_description)) code.AppendLine($"-- {_description}");

        code.AppendLine();

        // Add functions
        foreach (var (name, funcCode, isReadOnly) in _functions)
        {
            var flags = isReadOnly ? " flags=no-writes" : "";
            code.AppendLine($"redis.register_function('{name}',{flags}");
            code.AppendLine(funcCode);
            code.AppendLine(")");
            code.AppendLine();
        }

        return code.ToString();
    }

    private string BuildJavaScriptLibrary()
    {
        // Note: JavaScript support might require Redis Stack
        var code = new StringBuilder();

        code.AppendLine($"#!js name={_name}");

        if (!string.IsNullOrEmpty(_description)) code.AppendLine($"// {_description}");

        code.AppendLine();

        foreach (var (name, funcCode, isReadOnly) in _functions)
        {
            var flags = isReadOnly ? ", {flags: ['no-writes']}" : "";
            code.AppendLine($"redis.registerFunction('{name}',{flags}");
            code.AppendLine(funcCode);
            code.AppendLine(");");
            code.AppendLine();
        }

        return code.ToString();
    }
}