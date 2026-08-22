using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Fakes;

/// <summary>
/// Records each journey begun, whether one was still open when the publish
/// happened, and any failure reported against it (spec 027).
///
/// <para>
/// Hand-written rather than mocked because what matters is the *sequence* — a
/// journey begun and still running at the moment of the publish — and a call
/// count cannot tell that from one begun and already ended.
/// </para>
///
/// <para>
/// <b>Third copy of this fake in the repo</b>, after EventIngestion's and
/// StreamDistribution's. Left duplicated: collapsing three test projects onto a
/// shared helper is worth doing and is not this feature, which is about tracing
/// two publishers.
/// </para>
/// </summary>
public sealed class RecordingJourneyOrigin : IJourneyOrigin
{
    private readonly List<string> begun = [];

    public IReadOnlyList<string> Begun => begun;

    public int Open { get; private set; }

    public Exception? Failure { get; private set; }

    public IJourney Begin(string name)
    {
        begun.Add(name);
        Open++;
        return new Handle(this);
    }

    private sealed class Handle(RecordingJourneyOrigin owner) : IJourney
    {
        public void Failed(Exception exception) => owner.Failure = exception;

        public void Dispose() => owner.Open--;
    }
}
