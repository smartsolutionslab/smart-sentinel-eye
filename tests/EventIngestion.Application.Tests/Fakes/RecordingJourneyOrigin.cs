using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Records each journey the handler begins, and whether it was still open when
/// the publish happened (spec 026).
///
/// <para>
/// Hand-written rather than mocked because what matters is the *sequence* — a
/// journey begun and still running at the moment of the publish — and a call
/// count cannot tell that from one begun and already ended.
/// </para>
/// </summary>
public sealed class RecordingJourneyOrigin : IJourneyOrigin
{
    private readonly List<string> begun = [];

    public IReadOnlyList<string> Begun => begun;

    public int Open { get; private set; }

    public IDisposable Begin(string name)
    {
        begun.Add(name);
        Open++;
        return new Handle(this);
    }

    private sealed class Handle(RecordingJourneyOrigin owner) : IDisposable
    {
        public void Dispose() => owner.Open--;
    }
}
