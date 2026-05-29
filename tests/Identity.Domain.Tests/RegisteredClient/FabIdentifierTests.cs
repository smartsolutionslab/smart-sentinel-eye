using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class FabIdentifierTests
{
    [Theory]
    [InlineData("munich")]
    [InlineData("munich-1")]
    [InlineData("ab")]
    public void Accepts_well_formed_kebab_lowercase_names(string raw) =>
        FabIdentifier.From(raw).Value.ShouldBe(raw);

    [Theory]
    [InlineData("")]
    [InlineData("a")]                          // too short
    [InlineData("Munich")]                     // uppercase
    [InlineData("1munich")]                    // starts with digit
    [InlineData("munich_1")]                   // underscore
    public void Rejects_malformed_input(string raw)
    {
        Action act = () => FabIdentifier.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_name()
    {
        Action act = () => FabIdentifier.From(new string('a', FabIdentifier.MaximumLength + 1));
        act.ShouldThrow<ArgumentException>();
    }
}
