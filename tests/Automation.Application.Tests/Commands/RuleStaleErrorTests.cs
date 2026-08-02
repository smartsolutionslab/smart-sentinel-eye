using System.Net;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Tests.Commands;

/// <summary>
/// ADR-0047 gives every command its own error union, so the stale-version
/// case is declared once per mutating command.
///
/// <para>
/// Automation is the context where these unions live **inline** in the
/// <c>*Command.cs</c> files rather than in a separate <c>*Errors.cs</c> — a
/// glob over <c>Commands/*Errors.cs</c> misses them entirely, which is how
/// four of the eighteen mutating commands were nearly overlooked when this
/// spec was scoped. This test names both explicitly so the pair cannot drift
/// apart unnoticed.
/// </para>
/// </summary>
public class RuleStaleErrorTests
{
    private const string ExpectedCode = "RULE_STALE";

    private static ApiError[] EveryStaleCase() =>
    [
        new PublishRuleError.RuleStale("high-oee", 3, 4),
        new ArchiveRuleError.RuleStale("high-oee", 3, 4),
    ];

    [Fact]
    public void Both_mutating_commands_have_a_stale_case()
    {
        EveryStaleCase().Length.ShouldBe(2);
    }

    [Fact]
    public void Every_stale_case_reports_the_same_code()
    {
        EveryStaleCase().ShouldAllBe(error => error.Code == ExpectedCode);
    }

    [Fact]
    public void Every_stale_case_maps_to_409_conflict()
    {
        EveryStaleCase().ShouldAllBe(error => error.Status == HttpStatusCode.Conflict);
    }

    [Fact]
    public void The_message_names_the_rule_and_both_versions()
    {
        foreach (ApiError error in EveryStaleCase())
        {
            error.Message.ShouldContain("high-oee");
            error.Message.ShouldContain("3");
            error.Message.ShouldContain("4");
        }
    }

    [Fact]
    public void The_message_tells_the_caller_to_re_read_rather_than_retry()
    {
        EveryStaleCase().ShouldAllBe(error => error.Message.Contains("Re-read", StringComparison.Ordinal));
        EveryStaleCase().ShouldAllBe(error => !error.Message.Contains("Try again", StringComparison.OrdinalIgnoreCase));
    }
}
