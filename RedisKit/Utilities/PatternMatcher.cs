using System.Text.RegularExpressions;

namespace RedisKit.Utilities;

/// <summary>
///     Provides Redis glob pattern matching functionality
/// </summary>
internal static class PatternMatcher
{
    /// <summary>
    ///     Checks if a channel matches a Redis glob pattern
    /// </summary>
    /// <param name="input">Input string to match</param>
    /// <param name="pattern">Redis glob pattern</param>
    /// <returns>True if input matches pattern, false otherwise</returns>
    public static bool IsMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.IsNullOrEmpty(input);

        if (string.IsNullOrEmpty(input))
            return pattern == "*"; // Only * pattern matches empty input

        // Exact match
        if (pattern == input)
            return true;

        try
        {
            // Convert Redis glob pattern to regex
            var regexPattern = ConvertGlobToRegex(pattern);
            return Regex.IsMatch(input, regexPattern, RegexOptions.Compiled);
        }
        catch (RegexParseException)
        {
            // Invalid patterns return false instead of throwing
            return false;
        }
    }

    /// <summary>
    ///     Converts Redis glob pattern to .NET regex pattern
    /// </summary>
    /// <param name="globPattern">Redis glob pattern</param>
    /// <returns>Equivalent regex pattern</returns>
    private static string ConvertGlobToRegex(string globPattern)
    {
        var regex = "^";
        var i = 0;
        var len = globPattern.Length;
        var state = new PatternState();

        while (i < len)
        {
            var c = globPattern[i];

            if (state.InEscape)
            {
                regex += Regex.Escape(c.ToString());
                state.InEscape = false;
                i++;
                continue;
            }

            var (appendText, increment) = ProcessCharacter(c, globPattern, i, len, state);
            regex += appendText;
            i += increment;
        }

        regex += "$";
        return regex;
    }

    private static (string appendText, int increment) ProcessCharacter(
        char c, string pattern, int position, int length, PatternState state)
    {
        return c switch
        {
            '\\' => ProcessEscape(state),
            '*' => ProcessAsterisk(state),
            '?' => ProcessQuestionMark(state),
            '[' => ProcessOpenBracket(pattern, position, length, state),
            ']' => ProcessCloseBracket(state),
            '-' => ProcessDash(pattern, position, length, state),
            _ => (Regex.Escape(c.ToString()), 1)
        };
    }

    private static (string, int) ProcessEscape(PatternState state)
    {
        state.InEscape = true;
        return ("", 1);
    }

    private static (string, int) ProcessAsterisk(PatternState state)
    {
        return state.InClass ? ("\\*", 1) : (".*", 1);
    }

    private static (string, int) ProcessQuestionMark(PatternState state)
    {
        return state.InClass ? ("\\?", 1) : (".", 1);
    }

    private static (string, int) ProcessOpenBracket(string pattern, int position, int length, PatternState state)
    {
        if (state.InClass) return ("\\[", 1);

        state.InClass = true;
        var result = "[";
        var increment = 1;

        // Check for negation
        if (position + 1 < length && (pattern[position + 1] == '!' || pattern[position + 1] == '^'))
        {
            result += "^";
            increment = 2;
        }

        return (result, increment);
    }

    private static (string, int) ProcessCloseBracket(PatternState state)
    {
        if (state.InClass)
        {
            state.InClass = false;
            return ("]", 1);
        }

        return ("\\]", 1);
    }

    private static (string, int) ProcessDash(string pattern, int position, int length, PatternState state)
    {
        // Inside a character class, dash is range operator
        if (state.InClass && position > 0 && position < length - 1 &&
            pattern[position - 1] != '[' && pattern[position + 1] != ']')
            return ("-", 1);
        return ("\\-", 1);
    }

    /// <summary>
    ///     Validates if a pattern is a valid Redis glob pattern
    /// </summary>
    /// <param name="pattern">Pattern to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var bracketCount = 0;
        var inEscape = false;

        foreach (var c in pattern)
        {
            if (inEscape)
            {
                inEscape = false;
                continue;
            }

            if (c == '\\')
            {
                inEscape = true;
                continue;
            }

            if (c == '[')
                bracketCount++;
            else if (c == ']')
                bracketCount--;

            if (bracketCount < 0)
                return false;
        }

        return bracketCount == 0 && !inEscape;
    }

    private sealed class PatternState
    {
        public bool InClass { get; set; }
        public bool InEscape { get; set; }
    }
}