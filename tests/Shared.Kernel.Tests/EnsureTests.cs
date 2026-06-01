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

    [Fact]
    public void IsNotNull_passes_through_a_non_null_reference()
    {
        object instance = new();

        Ensure.That(instance).IsNotNull().AndReturn().ShouldBeSameAs(instance);
    }

    [Fact]
    public void IsNotNull_throws_ArgumentNullException_on_null()
    {
        object missing = null;

        Action act = () => Ensure.That(missing).IsNotNull();

        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void IsNotNull_names_the_argument_from_the_call_site()
    {
        List<int> numbers = null;

        ArgumentNullException thrown =
            Should.Throw<ArgumentNullException>(() => Ensure.That(numbers).IsNotNull());

        thrown.ParamName.ShouldBe("numbers");
    }

    [Fact]
    public void A_string_argument_still_binds_to_the_string_overload()
    {
        // The generic overload also accepts strings; the more specific string
        // overload must win so value-object invariant chains keep compiling.
        EnsuredString chained = Ensure.That("value");

        chained.IsNotNullOrWhiteSpace().AndReturn().ShouldBe("value");
    }

    [Fact]
    public void IsNotEmpty_passes_through_a_non_empty_guid()
    {
        Guid id = Guid.CreateVersion7();

        Ensure.That(id).IsNotEmpty().AndReturn().ShouldBe(id);
    }

    [Fact]
    public void IsNotEmpty_throws_on_the_empty_guid_naming_the_argument()
    {
        Guid identifier = Guid.Empty;

        ArgumentException thrown =
            Should.Throw<ArgumentException>(() => Ensure.That(identifier).IsNotEmpty());

        thrown.ParamName.ShouldBe("identifier");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AtLeast_accepts_values_at_or_above_the_floor(int value)
    {
        Ensure.That(value).AtLeast(1).AndReturn().ShouldBe(value);
    }

    [Fact]
    public void AtLeast_throws_below_the_floor()
    {
        Action act = () => Ensure.That(0).AtLeast(1);

        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(60000)]
    public void InRange_accepts_values_within_the_inclusive_bounds(int value)
    {
        Ensure.That(value).InRange(100, 60000).AndReturn().ShouldBe(value);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(60001)]
    public void InRange_throws_outside_the_bounds(int value)
    {
        Action act = () => Ensure.That(value).InRange(100, 60000);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Value_Satisfies_throws_when_the_predicate_fails()
    {
        Action act = () => Ensure.That(7).Satisfies(n => n % 2 == 0, "must be even");

        act.ShouldThrow<ArgumentException>();
    }
}
