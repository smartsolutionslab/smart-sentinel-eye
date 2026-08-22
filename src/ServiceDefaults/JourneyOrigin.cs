using System.Diagnostics;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Begins a journey as an <see cref="Activity"/>, so the messaging layer's own
/// propagation has a cause to carry (spec 026).
///
/// <para>
/// The source is named for the application because
/// <c>ConfigureOpenTelemetry</c> already registers
/// <c>AddSource(builder.Environment.ApplicationName)</c> — so this needs no
/// telemetry configuration of its own, and cannot be registered under a name
/// nothing exports.
/// </para>
/// </summary>
public sealed class JourneyOrigin : IJourneyOrigin, IDisposable
{
    /// <summary>
    /// Returned when nothing is listening. <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
    /// returns <see langword="null"/> whenever no listener has subscribed, which
    /// is the ordinary case in a unit test and in any service whose exporter is
    /// not wired. Handing back an inert handle keeps that out of every call site
    /// — a `using var` over a null is legal C# and silently does nothing, which
    /// would leave the caller unable to tell "not sampled" from "not started".
    /// </summary>
    private static readonly IDisposable NotListening = new Inert();

    private readonly ActivitySource source;

    public JourneyOrigin(string applicationName)
    {
        Ensure.That(applicationName).IsNotNullOrWhiteSpace();

        source = new ActivitySource(applicationName);
    }

    public IDisposable Begin(string name)
    {
        Ensure.That(name).IsNotNullOrWhiteSpace();

        // Producer rather than Internal: what follows is a message being sent,
        // and the span this parents is the receiving service's Consumer span.
        return source.StartActivity(name, ActivityKind.Producer) ?? NotListening;
    }

    public void Dispose() => source.Dispose();

    private sealed class Inert : IDisposable
    {
        public void Dispose()
        {
            // Nothing was started, so there is nothing to stop.
        }
    }
}
