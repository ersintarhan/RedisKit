using System;
using System.Text.RegularExpressions;

namespace RedisLib.Utilities
{
    /// <summary>
    /// Provides Redis glob pattern matching functionality
    /// </summary>
    internal static class PatternMatcher
    {
        /// <summary>
        /// Checks if a channel matches a Redis glob pattern
        /// </summary>
        /// <param name="pattern">Redis glob pattern</param>
        /// <param name="channel">Channel name to match</param>
        /// <returns>True if channel matches pattern, false otherwise</returns>
        public static bool IsMatch(string pattern, string channel)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(channel))
                return false;

            // Exact match
            if (pattern == channel)
                return true;

            // Convert Redis glob pattern to regex
            var regexPattern = ConvertGlobToRegex(pattern);
            return Regex.IsMatch(channel, regexPattern, RegexOptions.Compiled);
        }

        /// <summary>
        /// Converts Redis glob pattern to .NET regex pattern
        /// </summary>
        /// <param name="globPattern">Redis glob pattern</param>
        /// <returns>Equivalent regex pattern</returns>
        private static string ConvertGlobToRegex(string globPattern)
        {
            var regex = "^";
            var i = 0;
            var len = globPattern.Length;
            var inClass = false;
            var inEscape = false;

            while (i < len)
            {
                var c = globPattern[i];

                if (inEscape)
                {
                    regex += Regex.Escape(c.ToString());
                    inEscape = false;
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '\\':
                        inEscape = true;
                        i++;
                        break;

                    case '*':
                        if (!inClass)
                        {
                            regex += ".*";
                        }
                        else
                        {
                            regex += "\\*";
                        }
                        i++;
                        break;

                    case '?':
                        if (!inClass)
                        {
                            regex += ".";
                        }
                        else
                        {
                            regex += "\\?";
                        }
                        i++;
                        break;

                    case '[':
                        if (!inClass)
                        {
                            inClass = true;
                            regex += "[";
                            
                            // Check for negation
                            if (i + 1 < len && (globPattern[i + 1] == '!' || globPattern[i + 1] == '^'))
                            {
                                regex += "^";
                                i += 2;
                            }
                            else
                            {
                                i++;
                            }
                        }
                        else
                        {
                            regex += "\\[";
                            i++;
                        }
                        break;

                    case ']':
                        if (inClass)
                        {
                            inClass = false;
                            regex += "]";
                        }
                        else
                        {
                            regex += "\\]";
                        }
                        i++;
                        break;

                    case '-':
                        // Inside a character class, dash is range operator
                        if (inClass && i > 0 && i < len - 1 && 
                            globPattern[i - 1] != '[' && globPattern[i + 1] != ']')
                        {
                            regex += "-";
                        }
                        else
                        {
                            regex += "\\-";
                        }
                        i++;
                        break;

                    default:
                        regex += Regex.Escape(c.ToString());
                        i++;
                        break;
                }
            }

            regex += "$";
            return regex;
        }

        /// <summary>
        /// Validates if a pattern is a valid Redis glob pattern
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
    }
}