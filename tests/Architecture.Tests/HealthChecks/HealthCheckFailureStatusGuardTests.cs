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
