using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Resolution;

/// <summary>
/// Spec 301 (#2720) US2, T009 — new type, no implementation yet.
/// <c>ResolvedTextPairs</c> is the one place the pair-by-template rule is
/// written (plan.md "SystemVariables"): every producer
/// (<c>VariableValueChangedDomainEventHandler</c>,
/// <c>VariableArchivedDomainEventHandler</c>,
/// <c>GetOverlaySnapshotQueryHandler</c>) calls it, so the rule cannot
/// diverge between the push and the snapshot.
///
/// <para>
/// <b>Red by construction.</b> <c>ResolvedTextPairs</c> does not exist on
/// today's code, so this whole file fails to resolve.
/// </para>
/// </summary>
public class ResolvedTextPairsTests
{
    private static readonly Resolver RealResolver = new();

    private static Dictionary<string, VariableSnapshotEntry> Snapshot(
        params (string name, VariableValue value)[] entries)
    {
        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
        foreach ((string name, VariableValue value) in entries)
        {
            snapshot[name] = new VariableSnapshotEntry(value, BooleanLabels: null);
        }
        return snapshot;
    }

    /// <summary>The literal example from plan.md "Phase 4a, RED, US2".</summary>
    [Fact]
    public void Skips_shapes_drops_duplicates_and_keeps_order()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            ["", "A {{x}}", "B {{x}}", "A {{x}}"],
            RealResolver,
            Snapshot(("x", new VariableValue.NumberValue(7))));

        pairs.ShouldBe([("A {{x}}", "A 7"), ("B {{x}}", "B 7")]);
    }

    [Fact]
    public void Skips_an_empty_string_contributed_by_a_shape()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            [string.Empty, "OEE: {{oee}}%"],
            RealResolver,
            Snapshot(("oee", new VariableValue.NumberValue(82.5))));

        pairs.ShouldHaveSingleItem().ShouldBe(("OEE: {{oee}}%", "OEE: 82.5%"));
    }

    [Fact]
    public void Drops_a_template_already_seen_by_ordinal_comparison_keeping_the_first_occurrence()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            ["A {{x}}", "A {{x}}", "A {{x}}"],
            RealResolver,
            Snapshot(("x", new VariableValue.NumberValue(1))));

        pairs.ShouldHaveSingleItem().ShouldBe(("A {{x}}", "A 1"));
    }

    /// <summary>
    /// Ordinal, not ordinal-ignore-case: two templates differing only in case
    /// are distinct literal strings and both survive.
    /// </summary>
    [Fact]
    public void Treats_templates_differing_only_by_case_as_distinct()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            ["{{x}}", "{{X}}"],
            RealResolver,
            Snapshot());

        pairs.Count.ShouldBe(2);
    }

    [Fact]
    public void Keeps_the_order_of_first_appearance()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            ["Second {{b}}", "First {{a}}", "Second {{b}}"],
            RealResolver,
            Snapshot(("a", new VariableValue.StringValue("A")), ("b", new VariableValue.StringValue("B"))));

        pairs.Count.ShouldBe(2);
        pairs[0].Template.ShouldBe("Second {{b}}");
        pairs[1].Template.ShouldBe("First {{a}}");
    }

    [Fact]
    public void A_template_with_no_entry_in_the_snapshot_resolves_to_its_own_literal()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            ["Shift: {{shift}}"],
            RealResolver,
            Snapshot());

        pairs.ShouldHaveSingleItem().ShouldBe(("Shift: {{shift}}", "Shift: {{shift}}"));
    }

    [Fact]
    public void Returns_an_empty_list_when_every_template_is_empty()
    {
        IReadOnlyList<(string Template, string Resolved)> pairs = ResolvedTextPairs.Build(
            [string.Empty, string.Empty],
            RealResolver,
            Snapshot());

        pairs.ShouldBeEmpty();
    }
}
