using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// ADR-0189 ruling 3, ADR-0154 (issue #3149). The dead-letter queue is one shared broker queue, so
/// every host that serves <c>/health</c> reports its depth as <c>dead-letters</c>: a readiness
/// check that degrades but never fails the host, named for the thing it watches rather than the
/// dependency it reads through.
/// </summary>
[Collection(HostCaptureCollection.Name)]
public class DeadLetterCheckRegistrationTests
{
    private const string CheckName = "dead-letters";
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(60);

    [Theory]
    [MemberData(nameof(ServedHealthHosts.AllHosts), MemberType = typeof(ServedHealthHosts))]
    public void Every_served_health_host_registers_the_dead_letters_check_as_a_degrading_ready_check(
        string assemblyName)
    {
        var registrations = RegistrationsOf(assemblyName);

        var named = registrations.Where(candidate => candidate.Name == CheckName).ToList();

        named.Count.ShouldBe(1, $"{assemblyName} must register \"{CheckName}\" exactly once");
        named[0].FailureStatus.ShouldBe(HealthStatus.Degraded);
        named[0].Tags.ShouldContain("ready");

        foreach (var registration in registrations)
        {
            foreach (var forbidden in new[] { "rabbit", "amqp", "queue" })
            {
                registration.Name.ShouldNotContain(forbidden, Case.Insensitive);
            }
        }
    }

    private static List<HealthCheckRegistration> RegistrationsOf(string assemblyName)
    {
        var host = ServedHealthHosts.Resolve(assemblyName);

        using var composed = ComposedHost.Capture(
            host.AssemblyName,
            ServedHealthHosts.ContentRootOf(host),
            ServedHealthHosts.PlaceholderConfiguration,
            configureServices: null,
            CaptureTimeout);

        using var scope = composed.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }
}
