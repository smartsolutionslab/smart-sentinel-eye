using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// <c>TriggerSource</c> and <c>TriggerKind</c> were both <c>string</c>, sitting
/// next to each other in <c>Rule.Create</c>'s parameter list. Transposing them
/// compiled cleanly and changed behaviour silently — the same hazard
/// <c>HandlerDeconstructionTests</c> guards for record deconstruction.
///
/// <para>
/// Emptiness was already refused by <c>Rule.Create</c>, so that is not what
/// these types add. They add two things: the swap becomes a compile error, and
/// the length bound moves out of the EF configuration, where it was the only
/// thing enforcing it — a 17-character trigger source was constructible and
/// failed as a <c>DbUpdateException</c> at the far end of the request.
/// </para>
/// </summary>
public class TriggerValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_trigger_source_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => TriggerSource.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_trigger_kind_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => TriggerKind.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_trigger_source_longer_than_the_column_is_refused_before_the_database_sees_it()
    {
        string tooLong = new('x', TriggerSource.MaximumLength + 1);

        Action act = () => TriggerSource.From(tooLong);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_trigger_kind_longer_than_the_column_is_refused_before_the_database_sees_it()
    {
        string tooLong = new('x', TriggerKind.MaximumLength + 1);

        Action act = () => TriggerKind.From(tooLong);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_trigger_source_at_the_exact_column_width_is_accepted()
    {
        string atLimit = new('x', TriggerSource.MaximumLength);

        TriggerSource.From(atLimit).Value.Length.ShouldBe(TriggerSource.MaximumLength);
    }

    [Fact]
    public void A_trigger_kind_at_the_exact_column_width_is_accepted()
    {
        string atLimit = new('x', TriggerKind.MaximumLength);

        TriggerKind.From(atLimit).Value.Length.ShouldBe(TriggerKind.MaximumLength);
    }

    /// <summary>
    /// The bounds are the column widths in <c>RuleConfiguration</c>. If either
    /// column is widened, this fails and points at the type that must follow —
    /// a bound that silently disagrees with its column is worse than no bound,
    /// because it refuses values the database would have accepted.
    /// </summary>
    [Fact]
    public void The_bounds_match_the_columns_they_protect()
    {
        TriggerSource.MaximumLength.ShouldBe(16);
        TriggerKind.MaximumLength.ShouldBe(128);
    }

    [Fact]
    public void A_value_is_stored_exactly_as_given()
    {
        TriggerSource.From("mqtt").Value.ShouldBe("mqtt");
        TriggerKind.From("sensor.reading").Value.ShouldBe("sensor.reading");
    }
}
