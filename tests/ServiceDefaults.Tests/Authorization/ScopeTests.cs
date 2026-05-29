using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

public class ScopeTests
{
    [Fact]
    public void Every_scope_string_is_unique()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string scope in Scope.All)
        {
            seen.Add(scope).ShouldBeTrue($"duplicate scope: {scope}");
        }
    }

    [Theory]
    [InlineData("sse.cameras.read")]
    [InlineData("sse.cameras.write")]
    [InlineData("sse.events.publish")]
    [InlineData("sse.rules.write")]
    [InlineData("sse.identity.devices.write")]
    [InlineData("sse.identity.kiosks.write")]
    [InlineData("sse.audit.read")]
    public void Catalogue_contains_the_documented_v1_scope(string expected) =>
        Scope.All.ShouldContain(expected);

    [Fact]
    public void Every_scope_follows_the_sse_resource_verb_shape()
    {
        foreach (string scope in Scope.All)
        {
            string[] parts = scope.Split('.');
            parts.Length.ShouldBeGreaterThanOrEqualTo(3,
                $"scope '{scope}' must have at least sse.<resource>.<verb>");
            parts[0].ShouldBe("sse");
        }
    }
}
