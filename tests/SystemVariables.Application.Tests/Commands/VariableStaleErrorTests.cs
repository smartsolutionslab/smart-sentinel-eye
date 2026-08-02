using System.Net;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Commands;

/// <summary>
/// ADR-0047 gives every command its own error union, so the stale-version
/// case is declared once per mutating command. Only two commands mutate an
/// existing variable — set-value and archive — so unlike the revisioned
/// contexts this is a pair rather than a set of five.
/// </summary>
public class VariableStaleErrorTests
{
    private const string ExpectedCode = "VARIABLE_STALE";

    private static ApiError[] EveryStaleCase() =>
    [
        new SetVariableValueError.VariableStale("oeeLine1", 3, 4),
        new ArchiveVariableError.VariableStale("oeeLine1", 3, 4),
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
    public void The_message_names_the_variable_and_both_versions()
    {
        foreach (ApiError error in EveryStaleCase())
        {
            error.Message.ShouldContain("oeeLine1");
            error.Message.ShouldContain("3");
            error.Message.ShouldContain("4");
        }
    }

    // A variable is identified by name, not by a revision number — the message
    // has to name it, because an operator watching a value revert has no other
    // way to tell which variable lost the race.
    [Fact]
    public void The_message_tells_the_caller_to_re_read_rather_than_retry()
    {
        EveryStaleCase().ShouldAllBe(error => error.Message.Contains("Re-read", StringComparison.Ordinal));
        EveryStaleCase().ShouldAllBe(error => !error.Message.Contains("Try again", StringComparison.OrdinalIgnoreCase));
    }
}
