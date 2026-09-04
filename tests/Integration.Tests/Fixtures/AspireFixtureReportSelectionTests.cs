using Shouldly;
using Xunit;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// #1918 — the startup-timeout report has to name the resource that failed.
/// Pure decision logic, so these run without Docker or the fixture.
/// </summary>
public class AspireFixtureReportSelectionTests
{
    [Fact]
    public void A_service_that_exited_during_startup_is_reported()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal)
        {
            ["automation"] = "Finished",
            ["camera-catalog"] = "Running",
        };

        AspireFixture.SelectResourcesToReport(states, new(StringComparer.Ordinal)).ShouldBe(["automation"]);
    }

    [Fact]
    public void A_one_shot_job_that_finished_is_not_reported()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal) { ["migrations"] = "Finished" };

        AspireFixture.SelectResourcesToReport(states, new(StringComparer.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Rebuilders_that_never_started_are_not_reported()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal)
        {
            ["api-gateway-rebuilder"] = "NotStarted",
            ["automation-rebuilder"] = "NotStarted",
        };

        AspireFixture.SelectResourcesToReport(states, new(StringComparer.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_real_failure_is_not_buried_under_idle_rebuilders()
    {
        // The exact shape of the run that motivated this: one service exited,
        // eleven rebuilders idle. The old predicate reported the eleven and
        // omitted the one.
        Dictionary<string, string> states = new(StringComparer.Ordinal)
        {
            ["automation"] = "Finished",
            ["migrations"] = "Finished",
            ["api-gateway"] = "Running",
            ["api-gateway-rebuilder"] = "NotStarted",
            ["automation-rebuilder"] = "NotStarted",
            ["camera-catalog-rebuilder"] = "NotStarted",
        };

        AspireFixture.SelectResourcesToReport(states, new(StringComparer.Ordinal)).ShouldBe(["automation"]);
    }

    [Fact]
    public void A_resource_that_failed_outright_is_still_reported()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal)
        {
            ["identity"] = "FailedToStart",
            ["keycloak"] = "Running",
        };

        AspireFixture.SelectResourcesToReport(states, new(StringComparer.Ordinal)).ShouldBe(["identity"]);
    }

    [Fact]
    public void An_exited_resource_reports_its_exit_code()
    {
        // `Finished` alone cannot distinguish a clean shutdown from a death.
        // The exit code is the fact that decides it, and its absence is why
        // #1918's one occurrence could not be diagnosed afterwards.
        Dictionary<string, string> states = new(StringComparer.Ordinal)
        {
            ["automation"] = "Finished",
            ["camera-catalog"] = "Running",
        };
        Dictionary<string, int?> exitCodes = new(StringComparer.Ordinal)
        {
            ["automation"] = 139,
            ["camera-catalog"] = null,
        };

        string report = AspireFixture.FormatResourceStates(states, exitCodes);

        report.ShouldContain("automation: Finished (exit code 139)");
        report.ShouldContain("camera-catalog: Running");
        report.ShouldNotContain("camera-catalog: Running (exit");
    }
}
