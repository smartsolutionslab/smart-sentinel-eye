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

    public LastSuccessAt? LastSuccessAt { get; private set; }

    public StreamError? LastError { get; private set; }

    public ProvisionedAt ProvisionedAt { get; private set; } = null!;

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
            ProvisionedAt = ProvisionedAt.From(now),
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

    /// <summary>
    /// Retires the stream because its camera was retired (spec 028 FR-008).
    /// Terminal, and the row is kept — retirement records that hardware
    /// <em>was</em> there.
    ///
    /// <para>
    /// Idempotent for the same reason retiring a camera is: the announcement
    /// rides the outbox and can be redelivered, and a second call must not
    /// raise a second time. Any state can be retired — a camera can be pulled
    /// off the wall while its stream is healthy, degraded, offline or still
    /// provisioning.
    /// </para>
    /// </summary>
    public void Retire(IClock clock)
    {
        Ensure.That(clock).IsNotNull();

        if (State == StreamState.Retired)
        {
            return;
        }

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        LastError = null;
        State = StreamState.Retired;

        Raise(new StreamHealthChangedDomainEvent(
            Stream: Id,
            Camera: Camera,
            Fab: Fab,
            FromState: previous,
            ToState: StreamState.Retired,
            ChangedAt: now,
            Error: null));
    }

    /// <summary>
    /// Re-points the stream at a corrected source, because its camera's
    /// address changed (spec 029 FR-013).
    ///
    /// <para>
    /// The <see cref="Path"/> deliberately does not change. It derives from the
    /// camera identifier, which is immutable, so a correction moves what the
    /// path pulls from and nothing a viewer holds — anyone already watching
    /// keeps watching (FR-014). That is also why this is a re-point rather
    /// than a tear-down and re-provision.
    /// </para>
    ///
    /// <para>
    /// Idempotent, and terminal-safe. The announcement rides the outbox and can
    /// be redelivered, so re-pointing at the URL already held raises nothing;
    /// and a retired stream refuses, mirroring the health reports, because
    /// re-pointing hardware that has been retired changes nothing except the
    /// record.
    /// </para>
    /// </summary>
    public void RepointTo(StreamSourceUrl sourceUrl, IClock clock)
    {
        Ensure.That(sourceUrl).IsNotNull();
        Ensure.That(clock).IsNotNull();
        EnsureNotRetired(nameof(RepointTo));

        if (SourceUrl == sourceUrl)
        {
            return;
        }

        SourceUrl = sourceUrl;
    }

    public void ReportHealthy(TranscodeMode detectedMode, IClock clock)
    {
        Ensure.That(detectedMode).IsNotNull();
        Ensure.That(clock).IsNotNull();
        EnsureNotRetired(nameof(ReportHealthy));

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        TranscodeMode = detectedMode;
        LastSuccessAt = LastSuccessAt.From(now);
        LastError = null;
        State = StreamState.Healthy;

        if (previous != StreamState.Healthy)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                Fab: Fab,
                FromState: previous,
                ToState: StreamState.Healthy,
                ChangedAt: now,
                Error: null));
        }
    }

    public void ReportDegraded(StreamError error, IClock clock)
    {
        Ensure.That(error).IsNotNull();
        Ensure.That(clock).IsNotNull();
        EnsureNotRetired(nameof(ReportDegraded));

        StreamState previous = State;
        DateTimeOffset now = clock.UtcNow;

        LastError = error;
        State = StreamState.Degraded;

        if (previous != StreamState.Degraded)
        {
            Raise(new StreamHealthChangedDomainEvent(
                Stream: Id,
                Camera: Camera,
                Fab: Fab,
                FromState: previous,
                ToState: StreamState.Degraded,
                ChangedAt: now,
                Error: error));
        }
    }

    public void ReportOffline(StreamError error, IClock clock)
    {
        Ensure.That(error).IsNotNull();
        Ensure.That(clock).IsNotNull();
        EnsureNotRetired(nameof(ReportOffline));

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
                Fab: Fab,
                FromState: previous,
                ToState: StreamState.Offline,
                ChangedAt: now,
                Error: error));
        }
    }

    /// <summary>
    /// Retirement is terminal, and this is what makes it so. The watcher and a
    /// retirement race by construction: a sweep can read a stream, probe
    /// MediaMTX, and come back to report on it after the retirement committed.
    /// Before this guard <c>ReportHealthy</c> set <see cref="State"/>
    /// unconditionally, so that late probe would quietly move a retired stream
    /// back to Healthy and the watcher would resume announcing about hardware
    /// that no longer exists.
    ///
    /// <para>
    /// Throws rather than returning quietly: a health report on a retired
    /// stream is a caller that has not caught up, and the command handler
    /// translates the failure. Swallowing it would make the resurrection
    /// unobservable, which is the property that made it worth guarding.
    /// </para>
    /// </summary>
    private void EnsureNotRetired(string behaviour)
    {
        if (State == StreamState.Retired)
        {
            throw new InvalidOperationException(
                $"Stream {Id} is retired; {behaviour} cannot change a retired stream's state.");
        }
    }
}
