using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Resolution;

public class PlaceholderParserTests
{
    private static readonly string[] TwoExpected = ["oeeLine1", "lineStatus"];
    private static readonly string[] ThreeExpected = ["a", "b", "c"];

    [Fact]
    public void ExtractNames_finds_well_formed_placeholders()
    {
        IReadOnlyCollection<string> names =
            PlaceholderParser.ExtractNames("OEE: {{oeeLine1}}% Status: {{lineStatus}}");
        names.ShouldBe(TwoExpected);
    }

    [Fact]
    public void ExtractNames_returns_unique_names_in_first_occurrence_order()
    {
        IReadOnlyCollection<string> names =
            PlaceholderParser.ExtractNames("{{a}} {{b}} {{a}} {{c}} {{b}}");
        names.ShouldBe(ThreeExpected);
    }

    [Theory]
    [InlineData("no placeholders here")]
    [InlineData("{{1nope}}")]                  // starts with digit
    [InlineData("{{ withSpace }}")]             // whitespace inside
    [InlineData("{{has-dash}}")]                // invalid char
    [InlineData("{{tooLong" + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx}}")] // > 64
    public void ExtractNames_ignores_malformed_placeholders(string text)
    {
        PlaceholderParser.ExtractNames(text).ShouldBeEmpty();
    }

    [Fact]
    public void Substitute_replaces_well_formed_placeholders()
    {
        string output = PlaceholderParser.Substitute(
            "OEE: {{oee}}%", _ => "82.4");
        output.ShouldBe("OEE: 82.4%");
    }

    [Fact]
    public void Substitute_leaves_literal_when_resolver_returns_null()
    {
        string output = PlaceholderParser.Substitute(
            "OEE: {{oee}}% Status: {{status}}",
            name => name == "oee" ? "82.4" : null);
        output.ShouldBe("OEE: 82.4% Status: {{status}}");
    }

    [Fact]
    public void Substitute_leaves_malformed_placeholders_literal()
    {
        string output = PlaceholderParser.Substitute(
            "{{1nope}} {{ ws }} ok",
            _ => "REPLACED");
        output.ShouldBe("{{1nope}} {{ ws }} ok");
    }
}
