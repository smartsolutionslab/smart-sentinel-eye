using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Aggregate root for a live stream. One Stream per registered camera; the
/// camera reference is value-copied across the context boundary
/// (<see cref="CameraIdentifier"/>). Carries the four-state machine
/// (Provisioning → Healthy → Degraded → Offline + recovery edges) per spec
/// 002 FR-004. Invalid transitions throw; handlers translate to
/// <c>Result.Failure</c>.
/// </summary>
public sealed class Stream : AggregateRoot<StreamIdentifier>
{
    public CameraIdentifier Camera { get; private set; }

    public MediaMtxPath Path { get; private set; } = null!;

    public StreamState State { get; private set; } = null!;

    public TranscodeMode TranscodeMode { get; private set; } = null!;

    public Option<DateTimeOffset> LastSuccessAt { get; private set; }

    public Option<string> LastError { get; private set; }

    public DateTimeOffset ProvisionedAt { get; private set; }

    public OperatorIdentifier ProvisionedBy { get; private set; }

    private Stream() { }

    public static Stream Provision(
        CameraIdentifier camera,
        OperatorIdentifier provisionedBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        MediaMtxPath path = MediaMtxPath.For(camera);
        DateTimeOffset now = clock.UtcNow;
        Stream stream = new()
        {
            Id = StreamIdentifier.New(),
            Camera = camera,
            Path = path,
            State = StreamState.Provisioning,
            TranscodeMode = TranscodeMode.Unknown,
            LastSuccessAt = Option<DateTimeOffset>.None,
            LastError = Option<string>.None,
            ProvisionedAt = now,
            ProvisionedBy = provisionedBy,
        };

        stream.Raise(new StreamProvisionedDomainEvent(
            Stream: stream.Id,
            Camera: camera,
            Path: path,
            ProvisionedAt: now,
            ProvisionedBy: provisionedBy));

        return stream;
    }

    public void ReportHealthy(TranscodeMode detectedMode, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(detectedMode);
        ArgumentNullException.ThrowIfNull(clock);

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        TranscodeMode = detectedMode;
        LastSuccessAt = Option<DateTimeOffset>.Some(now);
        LastError = Option<string>.None;
        State = StreamState.Healthy;

        if (previous != StreamState.Healthy)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                FromState: previous,
                ToState: StreamState.Healthy,
                ChangedAt: now,
                Error: Option<string>.None));
        }
    }

    public void ReportDegraded(string error, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentNullException.ThrowIfNull(clock);

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        LastError = Option<string>.Some(error);
        State = StreamState.Degraded;

        if (previous != StreamState.Degraded)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                FromState: previous,
                ToState: StreamState.Degraded,
                ChangedAt: now,
                Error: Option<string>.Some(error)));
        }
    }

    public void ReportOffline(string error, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentNullException.ThrowIfNull(clock);

        if (State != StreamState.Degraded && State != StreamState.Offline)
        {
            throw new InvalidOperationException(
                $"Stream {Id} cannot transition from {State} directly to Offline; must pass through Degraded.");
        }

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        LastError = Option<string>.Some(error);
        State = StreamState.Offline;

        if (previous != StreamState.Offline)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                FromState: previous,
                ToState: StreamState.Offline,
                ChangedAt: now,
                Error: Option<string>.Some(error)));
        }
    }
}
