#nullable enable
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
///
/// <para>
/// <see cref="LastSuccessAt"/> and <see cref="LastError"/> use nullable
/// reference types rather than <c>Option&lt;T&gt;</c> per ADR-0048. The
/// deviation is documented inline: EF Core's value-converter API for
/// <c>Option&lt;T&gt; ↔ T?</c> requires the internal-API
/// <c>convertsNulls</c> overload (EF1001), and the canonical model-metadata
/// override (<c>IsNullable = true</c> on a non-nullable property) is
/// rejected at runtime. Nullable types are pragmatic for these two
/// EF-persisted columns; <c>Option&lt;T&gt;</c> stays the rule for
/// invariant-bearing fields.
/// </para>
/// </summary>
public sealed class Stream : AggregateRoot<StreamIdentifier>
{
    /// <summary>
    /// The fab this stream's camera belongs to (spec 016). Nullable because
    /// streams provisioned before this feature have none yet and cannot be
    /// backfilled in SQL — the cameras live in another database — so they
    /// acquire it at runtime instead. A stream with no fab is visible to
    /// nobody (FR-009).
    ///
    /// <para>
    /// There is no setter and no <c>MoveToFab</c>: a stream's fab is its
    /// camera's, and a camera cannot change fab (spec 015 FR-004), so the
    /// value can never legitimately change once known. FR-002 requires the
    /// two never to differ, and the guarantee is that the aggregate has no
    /// way to express it.
    /// </para>
    /// </summary>
    public FabIdentifier? Fab { get; private set; }

    public CameraIdentifier Camera { get; private set; }

    public MediaMtxPath Path { get; private set; } = null!;

    /// <summary>
    /// The RTSP source this path pulls. Persisted so the startup reconciler can
    /// re-create the path in MediaMTX after it loses its runtime configuration.
    /// </summary>
    public StreamSourceUrl SourceUrl { get; private set; } = null!;

    public StreamState State { get; private set; } = null!;

    public TranscodeMode TranscodeMode { get; private set; } = null!;

    public DateTimeOffset? LastSuccessAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset ProvisionedAt { get; private set; }

    public OperatorIdentifier ProvisionedBy { get; private set; }

    private Stream() { }

    public static Stream Provision(
        FabIdentifier fab,
        CameraIdentifier camera,
        StreamSourceUrl sourceUrl,
        OperatorIdentifier provisionedBy,
        IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(sourceUrl).IsNotNull();
        Ensure.That(clock).IsNotNull();

        MediaMtxPath path = MediaMtxPath.For(camera);
        DateTimeOffset now = clock.UtcNow;
        Stream stream = new()
        {
            Id = StreamIdentifier.New(),
            Fab = fab,
            Camera = camera,
            Path = path,
            SourceUrl = sourceUrl,
            State = StreamState.Provisioning,
            TranscodeMode = TranscodeMode.Unknown,
            LastSuccessAt = null,
            LastError = null,
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

    /// <summary>
    /// Fills in the fab of a stream provisioned before spec 016 (FR-008).
    ///
    /// <para>
    /// One-way, and that is the whole of the guarantee: it moves a stream from
    /// "fab unknown" to "fab known" and refuses anything else. FR-002 says a
    /// stream's fab and its camera's must never differ; a stream that already
    /// has one took it from its camera at provisioning, and a camera cannot
    /// change fab (spec 015 FR-004), so there is no legitimate second call.
    /// A plain setter would allow one.
    /// </para>
    /// </summary>
    public void AttributeToFab(FabIdentifier fab)
    {
        Ensure.That(fab).IsNotNull();

        if (Fab is not null)
        {
            throw new InvalidOperationException(
                $"Stream {Id} already belongs to fab {Fab}; a stream's fab is its camera's and cannot be reassigned.");
        }

        Fab = fab;
    }

    public void ReportHealthy(TranscodeMode detectedMode, IClock clock)
    {
        Ensure.That(detectedMode).IsNotNull();
        Ensure.That(clock).IsNotNull();

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        TranscodeMode = detectedMode;
        LastSuccessAt = now;
        LastError = null;
        State = StreamState.Healthy;

        if (previous != StreamState.Healthy)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                FromState: previous,
                ToState: StreamState.Healthy,
                ChangedAt: now,
                Error: null));
        }
    }

    public void ReportDegraded(string error, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Ensure.That(clock).IsNotNull();

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        LastError = error;
        State = StreamState.Degraded;

        if (previous != StreamState.Degraded)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                FromState: previous,
                ToState: StreamState.Degraded,
                ChangedAt: now,
                Error: error));
        }
    }

    public void ReportOffline(string error, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Ensure.That(clock).IsNotNull();

        if (State != StreamState.Degraded && State != StreamState.Offline)
        {
            throw new InvalidOperationException($"Stream {Id} cannot transition from {State} directly to Offline; must pass through Degraded.");
        }

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        LastError = error;
        State = StreamState.Offline;

        if (previous != StreamState.Offline)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                FromState: previous,
                ToState: StreamState.Offline,
                ChangedAt: now,
                Error: error));
        }
    }
}
