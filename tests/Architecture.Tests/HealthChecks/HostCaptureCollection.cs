namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), plan §3.4. <see cref="ComposedHost"/> subscribes to the
/// process-global <c>Microsoft.Extensions.Hosting</c> <see
/// cref="System.Diagnostics.DiagnosticListener"/> while it runs a host's real
/// entry point. The capture's own thread filter (plan §3.2.3) stops it from
/// misattributing another test's host-building event, but two captures (or a
/// capture concurrent with any other test that builds a host — e.g.
/// <c>SystemVariableReadScopeTests</c>, <c>SystemVariableRouteShadowingTests</c>,
/// <c>DatabaseCommandLogLevelTests</c>, <c>WhepAuthorizeCeilingTests</c>, none
/// of which carry this collection) racing the listener subscription itself is
/// still worth avoiding deterministically rather than relying on the filter
/// alone. <c>DisableParallelization = true</c> makes xUnit run every test
/// carrying this collection after all parallel collections, alone — belt and
/// braces alongside the thread filter, not a replacement for it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
#pragma warning disable CA1711 // xUnit convention requires the "Collection" suffix
public class HostCaptureCollection
#pragma warning restore CA1711
{
    public const string Name = "host-capture";

    // xUnit reads the attribute off this type; nothing constructs it.
    protected HostCaptureCollection()
    {
    }
}
