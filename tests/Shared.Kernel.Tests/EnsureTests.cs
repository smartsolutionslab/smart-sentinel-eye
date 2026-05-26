using System.Text.RegularExpressions;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class EnsureTests
{
    [Fact]
    public void IsNotNullOrWhiteSpace_passes_through_non_empty_strings()
    {
        string result = Ensure.That("hello").IsNotNullOrWhiteSpace().AndReturn();

        result.ShouldBe("hello");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsNotNullOrWhiteSpace_throws_on_blank(string value)
    {
        Action act = () => Ensure.That(value).IsNotNullOrWhiteSpace().AndReturn();

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void HasMinLength_accepts_strings_at_or_above_the_floor()
    {
        Ensure.That("abc").HasMinLength(3).AndReturn().ShouldBe("abc");
        Ensure.That("abcd").HasMinLength(3).AndReturn().ShouldBe("abcd");
    }

    [Fact]
    public void HasMinLength_throws_when_too_short()
    {
        Action act = () => Ensure.That("ab").HasMinLength(3).AndReturn();

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void HasMaxLength_accepts_strings_at_or_below_the_ceiling()
    {
        Ensure.That("abc").HasMaxLength(3).AndReturn().ShouldBe("abc");
        Ensure.That("ab").HasMaxLength(3).AndReturn().ShouldBe("ab");
    }

    [Fact]
    public void HasMaxLength_throws_when_too_long()
    {
        Action act = () => Ensure.That("abcd").HasMaxLength(3).AndReturn();

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void StartsWith_accepts_matching_prefix_case_insensitively_when_asked()
    {
        Ensure.That("RTSP://cam").StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase).AndReturn()
            .ShouldBe("RTSP://cam");
    }

    [Fact]
    public void StartsWith_throws_when_prefix_does_not_match()
    {
        Action act = () => Ensure.That("http://cam").StartsWith("rtsp://", StringComparison.Ordinal).AndReturn();

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Matches_accepts_strings_matching_the_pattern()
    {
        Regex pattern = new("^[a-z]+$");

        Ensure.That("hello").Matches(pattern, "must be lowercase letters").AndReturn().ShouldBe("hello");
    }

    [Fact]
    public void Matches_throws_when_pattern_fails()
    {
        Regex pattern = new("^[a-z]+$");

        Action act = () => Ensure.That("Hello1").Matches(pattern, "must be lowercase letters").AndReturn();

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Satisfies_accepts_inputs_that_pass_the_predicate()
    {
        Ensure.That("OK").Satisfies(value => value.Length == 2, "must be two chars").AndReturn().ShouldBe("OK");
    }

    [Fact]
    public void Satisfies_throws_when_predicate_returns_false()
    {
        Action act = () => Ensure.That("X").Satisfies(value => value.Length == 2, "must be two chars").AndReturn();

        act.ShouldThrow<ArgumentException>();
    }
}
