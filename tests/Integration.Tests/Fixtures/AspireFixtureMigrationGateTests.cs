using Shouldly;
using Xunit;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// #2064 — the wait on `migrations` has to ask how it finished, not only that
/// it finished. Pure decision logic, so these run without Docker or the
/// fixture, alongside <see cref="AspireFixtureReportSelectionTests"/>.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AspireFixtureMigrationGateTests
{
    [Fact]
    public void A_migration_that_exited_non_zero_is_a_failure()
    {
        AspireFixture.ExitedNonZero(134).ShouldBeTrue();
    }

    [Fact]
    public void A_migration_that_exited_cleanly_is_not_a_failure()
    {
        AspireFixture.ExitedNonZero(0).ShouldBeFalse();
    }

    [Fact]
    public void A_migration_whose_exit_code_was_never_observed_is_not_a_failure()
    {
        // A *present* null, not an absent key and not an empty dictionary:
        // the wait reads `ExitCode` straight off the snapshot it matched, and
        // an unobserved code arrives there as null. Reading that as a failure
        // would abort every healthy boot in the repository — and an absent-key
        // spelling would pass for the wrong reason, which was #2061's blocker.
        AspireFixture.ExitedNonZero((int?)null).ShouldBeFalse();
    }

    [Fact]
    public void A_negative_exit_code_is_a_failure()
    {
        // The code #2061's phase 5 actually observed on Windows, against CI's
        // 134. A `> 0` rule would pass every other test here and let this one
        // through as a clean finish.
        AspireFixture.ExitedNonZero(-532462766).ShouldBeTrue();
    }

    [Fact]
    public void The_failure_message_names_the_code_before_it_shows_the_log()
    {
        string message = AspireFixture.FormatMigrationFailureMessage(134, "Unhandled exception. System.InvalidOperationException");

        message.ShouldContain("134");
        message.ShouldContain("Unhandled exception. System.InvalidOperationException");
        message.IndexOf("134", StringComparison.Ordinal)
            .ShouldBeLessThan(message.IndexOf("Unhandled exception. System.InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public void The_failure_message_still_leads_with_the_code_when_no_log_was_served()
    {
        // FR-003's second clause, and the one CI exercises: #2061 saw 2,628
        // `(no logs captured)` placeholders on the Linux runner while Windows
        // served a log for every resource. The message has to carry the verdict
        // on its own when the log is a placeholder.
        string message = AspireFixture.FormatMigrationFailureMessage(134, "(no logs captured)");

        message.ShouldContain("134");
        message.ShouldContain("a non-zero exit is a failure, not a clean finish.");
        message.IndexOf("134", StringComparison.Ordinal)
            .ShouldBeLessThan(message.IndexOf("(no logs captured)", StringComparison.Ordinal));
    }
}
