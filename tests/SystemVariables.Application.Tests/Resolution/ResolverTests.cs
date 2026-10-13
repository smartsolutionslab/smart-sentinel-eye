using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Resolution;

public class ResolverTests
{
    private static readonly Resolver Sut = new();

    private static Dictionary<string, VariableSnapshotEntry> Snapshot(
        params (string name, VariableValue value, BooleanLabels? labels)[] entries)
    {
        Dictionary<string, VariableSnapshotEntry> d = new(StringComparer.Ordinal);
        foreach ((var name, var value, var labels) in entries)
        {
            d[name] = new VariableSnapshotEntry(value, labels);
        }
        return d;
    }

    [Fact]
    public void Substitutes_String_values()
    {
        var output = Sut.Resolve(
            "Status: {{status}}",
            Snapshot(("status", new VariableValue.StringValue("Healthy"), null)));
        output.ShouldBe("Status: Healthy");
    }

    [Fact]
    public void Substitutes_Number_values_culture_invariantly()
    {
        var output = Sut.Resolve(
            "OEE: {{oee}}%",
            Snapshot(("oee", new VariableValue.NumberValue(82.4), null)));
        output.ShouldBe("OEE: 82.4%");
    }

    [Fact]
    public void Substitutes_Boolean_values_via_labels()
    {
        var labels = BooleanLabels.From("Running", "Stopped");
        var output = Sut.Resolve(
            "Line: {{lineState}}",
            Snapshot(("lineState", new VariableValue.BooleanValue(true), labels)));
        output.ShouldBe("Line: Running");
    }

    [Fact]
    public void Falls_back_to_default_BooleanLabels_when_entry_has_none()
    {
        var output = Sut.Resolve(
            "Line: {{lineState}}",
            Snapshot(("lineState", new VariableValue.BooleanValue(false), null)));
        output.ShouldBe("Line: No");
    }

    [Fact]
    public void Leaves_unset_variable_placeholders_literal()
    {
        // Snapshot is missing the variable entirely — same effect as Unset per FR-011.
        var output = Sut.Resolve("OEE: {{oee}}%", Snapshot());
        output.ShouldBe("OEE: {{oee}}%");
    }

    [Fact]
    public void Mixes_resolved_and_literal_placeholders()
    {
        var output = Sut.Resolve(
            "OEE: {{oee}}% Status: {{status}}",
            Snapshot(("oee", new VariableValue.NumberValue(82.4), null)));
        output.ShouldBe("OEE: 82.4% Status: {{status}}");
    }
}
