using FluentAssertions;
using RedisKit.Utilities;
using Xunit;

namespace RedisKit.Tests.Utilities;

public class PatternMatcherTests
{
    [Theory]
    [InlineData("test*", "test123", true)]
    [InlineData("test*", "testing", true)]
    [InlineData("test*", "test", true)]
    [InlineData("test*", "tes", false)]
    [InlineData("test*", "other", false)]
    [InlineData("*test", "mytest", true)]
    [InlineData("*test", "test", true)]
    [InlineData("*test", "testing", false)]
    [InlineData("test?", "test1", true)]
    [InlineData("test?", "testa", true)]
    [InlineData("test?", "test", false)]
    [InlineData("test?", "test12", false)]
    [InlineData("test[abc]", "testa", true)]
    [InlineData("test[abc]", "testb", true)]
    [InlineData("test[abc]", "testc", true)]
    [InlineData("test[abc]", "testd", false)]
    [InlineData("test[a-z]", "testa", true)]
    [InlineData("test[a-z]", "testz", true)]
    [InlineData("test[a-z]", "testA", false)]
    [InlineData("test[0-9]", "test5", true)]
    [InlineData("test[0-9]", "testa", false)]
    public void IsMatch_Should_Match_Patterns_Correctly(string pattern, string input, bool expectedMatch)
    {
        // Act
        var result = PatternMatcher.IsMatch(input, pattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData("user:*:profile", "user:123:profile", true)]
    [InlineData("user:*:profile", "user:456:profile", true)]
    [InlineData("user:*:profile", "user:abc:profile", true)]
    [InlineData("user:*:profile", "user:123:settings", false)]
    [InlineData("cache:*:*", "cache:user:data", true)]
    [InlineData("cache:*:*", "cache:session:info", true)]
    [InlineData("cache:*:*", "cache:user", false)]
    [InlineData("events:?:*", "events:1:created", true)]
    [InlineData("events:?:*", "events:a:updated", true)]
    [InlineData("events:?:*", "events:12:deleted", false)]
    public void IsMatch_Should_Handle_Redis_Key_Patterns(string pattern, string input, bool expectedMatch)
    {
        // Act
        var result = PatternMatcher.IsMatch(input, pattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData("", "test", false)]
    [InlineData("test", "", false)]
    [InlineData("", "", true)]
    [InlineData("*", "", true)]
    [InlineData("*", "anything", true)]
    [InlineData("?", "", false)]
    [InlineData("?", "a", true)]
    public void IsMatch_Should_Handle_Edge_Cases(string pattern, string input, bool expectedMatch)
    {
        // Act
        var result = PatternMatcher.IsMatch(input, pattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData("test[")]
    [InlineData("test]")]
    [InlineData("test[z-a]")]
    public void IsMatch_Should_Handle_Invalid_Patterns_Gracefully(string pattern)
    {
        // Act & Assert - Should not throw exceptions
        var exception = Record.Exception(() => PatternMatcher.IsMatch("test", pattern));
        
        // Should not throw an exception
        exception.Should().BeNull();
    }

    [Fact]
    public void IsMatch_Should_Be_Case_Sensitive()
    {
        // Arrange
        var pattern = "Test*";
        var input1 = "Testing";
        var input2 = "testing";

        // Act
        var result1 = PatternMatcher.IsMatch(input1, pattern);
        var result2 = PatternMatcher.IsMatch(input2, pattern);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeFalse();
    }

    [Theory]
    [InlineData("test**", "testing", true)]
    [InlineData("**test", "mytest", true)]
    [InlineData("te**st", "test", true)]
    [InlineData("te**st", "teabcst", true)]
    public void IsMatch_Should_Handle_Multiple_Wildcards(string pattern, string input, bool expectedMatch)
    {
        // Act
        var result = PatternMatcher.IsMatch(input, pattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData("test\\*", "test*", true)]
    [InlineData("test\\*", "testing", false)]
    [InlineData("test\\?", "test?", true)]
    [InlineData("test\\?", "testa", false)]
    public void IsMatch_Should_Handle_Escaped_Characters(string pattern, string input, bool expectedMatch)
    {
        // Act
        var result = PatternMatcher.IsMatch(input, pattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData(null, "test")]
    [InlineData("test", null)]
    [InlineData(null, null)]
    public void IsMatch_Should_Handle_Null_Arguments(string? pattern, string? input)
    {
        // Act & Assert - Should not throw null reference exceptions
        var act = () => PatternMatcher.IsMatch(input!, pattern!);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("prefix:*", new[] { "prefix:key1", "prefix:key2", "other:key1" }, new[] { "prefix:key1", "prefix:key2" })]
    [InlineData("*:suffix", new[] { "key1:suffix", "key2:suffix", "key1:other" }, new[] { "key1:suffix", "key2:suffix" })]
    [InlineData("exact", new[] { "exact", "exactish", "not_exact" }, new[] { "exact" })]
    public void IsMatch_Should_Filter_Collections_Correctly(string pattern, string[] inputs, string[] expectedMatches)
    {
        // Act
        var matches = inputs.Where(input => PatternMatcher.IsMatch(input, pattern)).ToArray();

        // Assert
        matches.Should().BeEquivalentTo(expectedMatches);
    }
}