namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), T007. The capture-failure cases plan §6 and FR-004
/// specify: a host whose composition throws must fail loudly with the host's
/// own exception, never vacuously with zero registrations, and an entry
/// point that never reaches <c>Build()</c> must fail loudly too. Carries the
/// same <see cref="HostCaptureCollection"/> attribute as
/// <see cref="HealthCheckFailureStatusGuardTests"/>, in its own file so the
/// two test-writer tasks that produced them (T006, T007) own disjoint files
/// (plan's own note on the two tasks).
///
/// <para>
/// <b>The capture mechanism's exception path is observed, not assumed.</b>
/// <see cref="ComposedHost.Capture"/> waits on a faulted
/// <see cref="TaskCompletionSource{TResult}"/> via <c>Task.Wait(TimeSpan)</c>,
/// which rethrows as <see cref="AggregateException"/> rather than the bare
/// exception — confirmed by a scratch probe outside the repository, the same
/// kind of direct observation spec §1.2 used for the capture mechanism
/// itself. The host's own exception is <see cref="AggregateException.InnerException"/>.
/// </para>
/// </summary>
[Collection(HostCaptureCollection.Name)]
public class ComposedHostCaptureTests
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// FR-004: a host that cannot build fails loudly with its own exception,
    /// never vacuously with zero registrations. An empty configuration
    /// dictionary makes <c>AddBearerAuthentication</c> throw before
    /// <c>Build()</c> is reached (spec §1.3), so <c>HostBuilding</c>/<c>HostBuilt</c>
    /// never fire and the capture mechanism's own hook plays no part in this
    /// failure.
    /// </summary>
    [Fact]
    public void A_host_missing_required_configuration_fails_capture_with_its_own_exception()
    {
        ServedHealthHosts.Host host = ServedHealthHosts.Resolve("SmartSentinelEye.Automation.Api");

        AggregateException thrown = Should.Throw<AggregateException>(() => ComposedHost.Capture(
            host.AssemblyName,
            ServedHealthHosts.ContentRootOf(host),
            new Dictionary<string, string?>(),
            configureServices: null,
            CaptureTimeout));

        thrown.InnerException.ShouldNotBeNull();
        thrown.InnerException.Message.ShouldContain("Keycloak base URL not found");
    }

    /// <summary>
    /// FR-004's other failure shape: an entry point that returns without ever
    /// reaching <c>HostBuilt</c>. <c>Architecture.Tests</c>' own assembly is
    /// used as the subject — its Microsoft.NET.Test.Sdk-generated entry point
    /// returns <c>void</c> immediately when invoked directly with no test-host
    /// arguments (confirmed by a scratch probe: it neither throws nor starts a
    /// recursive test run), which is exactly the "returned without building a
    /// host" shape this guards, and the probe outside the repository confirmed
    /// it returns in well under a second rather than hanging.
    /// <para>
    /// Safe under the pinned xUnit 2.9.3, whose SDK-generated <c>Main</c> is a
    /// thin shim; revisit on a move to xunit.v3, where that <c>Main</c>
    /// becomes the in-process test runner itself and invoking it directly
    /// would no longer "return without building a host" — it would run the
    /// suite.
    /// </para>
    /// </summary>
    [Fact]
    public void An_entry_point_that_returns_without_building_a_host_fails_capture()
    {
        AggregateException thrown = Should.Throw<AggregateException>(() => ComposedHost.Capture(
            "SmartSentinelEye.Architecture.Tests",
            RepositorySource.Root().FullName,
            new Dictionary<string, string?>(),
            configureServices: null,
            CaptureTimeout));

        thrown.InnerException.ShouldNotBeNull();
        thrown.InnerException.Message.ShouldContain("returned without building a host");
    }
}
