using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

/// <summary>
/// Exhaustive coverage of the two-state machine: every allowed and
/// forbidden transition gets a pinning test.
/// </summary>
public class VariableStateMachineTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static IClock Clock => new VariableBuilder.TestClock(FixedMoment);

    private static OperatorIdentifier By => OperatorIdentifier.From(Guid.CreateVersion7());

    // Allowed: Define → Defined.
    [Fact]
    public void Define_yields_Defined_state()
    {
        Domain.Variable.Variable v = new VariableBuilder().Build();
        v.State.ShouldBe(VariableState.Defined);
    }

    // Allowed: Defined --SetValue--> Defined.
    [Fact]
    public void SetValue_on_Defined_stays_Defined()
    {
        VariableBuilder b = new VariableBuilder().OfType(VariableType.Number);
        Domain.Variable.Variable v = b.Build();
        v.SetValue(new VariableValue.NumberValue(1.0), By, Clock);
        v.State.ShouldBe(VariableState.Defined);
    }

    // Allowed: Defined --Archive--> Archived.
    [Fact]
    public void Archive_on_Defined_transitions_to_Archived()
    {
        Domain.Variable.Variable v = new VariableBuilder().Build();
        v.Archive(By, Clock);
        v.State.ShouldBe(VariableState.Archived);
    }

    // Idempotent: Archived --Archive--> Archived (no event).
    [Fact]
    public void Archive_on_Archived_is_idempotent_and_silent()
    {
        Domain.Variable.Variable v = new VariableBuilder().Build();
        v.Archive(By, Clock);
        v.ClearPendingEvents();
        v.Archive(By, Clock);
        v.State.ShouldBe(VariableState.Archived);
        v.PendingEvents.ShouldBeEmpty();
    }

    // Forbidden: Archived --SetValue--> *.
    [Fact]
    public void SetValue_on_Archived_throws()
    {
        VariableBuilder b = new VariableBuilder().OfType(VariableType.Number);
        Domain.Variable.Variable v = b.Build();
        v.Archive(By, Clock);

        Action act = () => v.SetValue(new VariableValue.NumberValue(1.0), By, Clock);
        act.ShouldThrow<InvalidOperationException>();
    }

    // The fab is orthogonal to the state machine: no transition touches it.
    [Fact]
    public void The_fab_survives_value_changes_and_archiving()
    {
        Domain.Variable.Variable v = new VariableBuilder()
            .WithFab("dresden")
            .OfType(VariableType.Number)
            .Build();

        v.Fab.Value.ShouldBe("dresden");

        v.SetValue(new VariableValue.NumberValue(1.0), By, Clock);
        v.Fab.Value.ShouldBe("dresden");

        v.Archive(By, Clock);
        v.Fab.Value.ShouldBe("dresden");
    }

    [Fact]
    public void The_aggregate_exposes_no_way_to_move_a_variable_between_fabs()
    {
        // Structural: relocation is out of scope by decision (T006) — moving a
        // variable would silently repoint every overlay resolving it. Asserts
        // the setter is not public rather than that no method mentions "Fab",
        // which would match the property getter and pass vacuously.
        System.Reflection.PropertyInfo fab =
            typeof(Domain.Variable.Variable).GetProperty("Fab")!;

        fab.SetMethod?.IsPublic.ShouldNotBe(true);
    }
}
