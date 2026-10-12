using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571). ADR-0154 clause 1: <c>/health</c> answers <c>200</c>
/// unless the process cannot serve at all, so no registered check may carry
/// the framework's default <see cref="HealthStatus.Unhealthy"/> failure
/// status, except the framework's own "self" liveness check (spec §5).
///
/// <para>
/// <b>T003's ten-host theory arrives green against the stub rule, and that
/// is not evidence of anything</b> (plan §7 step 1) — the stub reports no
/// offenders for any input. Its bite is proven later, by counterfactual
/// (SC-003, SC-004), following the precedent set by
/// <c>DependencyHealthCheckGuardTests</c>' own doc comment.
/// </para>
///
/// <para>
/// <b>T004 is phase 4a's red test.</b> It plants a violation through the
/// capture mechanism's own <c>HostBuilding</c> hook — never by editing
/// production code — and must fail on the stub rule's Shouldly assertion,
/// not on a compile error or an exception from the capture mechanism
/// itself.
/// </para>
/// </summary>
[Collection(HostCaptureCollection.Name)]
public class HealthCheckFailureStatusGuardTests
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(60);

    [Theory]
    [MemberData(nameof(ServedHealthHosts.AllHosts), MemberType = typeof(ServedHealthHosts))]
    public void Every_health_check_on_a_host_serving_health_declares_a_failure_status_other_than_unhealthy(
        string assemblyName)
    {
        List<string> offenders = CaptureOffenders(assemblyName, configureServices: null);

        offenders.ShouldBeEmpty(
            $"{assemblyName} registers a health check whose failure status is the framework default "
            + "(Unhealthy), other than the exempt \"self\" liveness check (ADR-0154 clause 1): "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// FR-006, T004. The planted check is added through
    /// <see cref="ComposedHost.Capture"/>'s own <c>HostBuilding</c> hook, not
    /// by editing <c>Program.cs</c> or <c>ServiceDefaults</c>. Run against
    /// T003's stub (<see cref="HealthCheckFailureStatusRule"/> returning
    /// <c>[]</c> unconditionally), this must fail on the assertion below.
    /// </summary>
    [Fact]
    public void The_guard_reports_a_check_registered_without_a_failure_status()
    {
        List<string> offenders = CaptureOffenders(
            "SmartSentinelEye.ApiGateway",
            configureServices: builder => builder.ConfigureServices(
                services => services.AddHealthChecks().AddCheck("planted", () => HealthCheckResult.Healthy())));

        offenders.ShouldHaveSingleItem();
        offenders[0].ShouldContain("planted");
        offenders[0].ShouldContain(nameof(HealthStatus.Unhealthy));
    }

    private static List<string> CaptureOffenders(string assemblyName, Action<IHostBuilder>? configureServices)
    {
        ServedHealthHosts.Host host = ServedHealthHosts.Resolve(assemblyName);

        using IHost composed = ComposedHost.Capture(
            host.AssemblyName,
            ServedHealthHosts.ContentRootOf(host),
            ServedHealthHosts.PlaceholderConfiguration,
            configureServices,
            CaptureTimeout);

        using IServiceScope scope = composed.Services.CreateScope();
        HealthCheckServiceOptions options =
            scope.ServiceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        return [.. HealthCheckFailureStatusRule.Offenders([.. options.Registrations], scope.ServiceProvider)];
    }
}
