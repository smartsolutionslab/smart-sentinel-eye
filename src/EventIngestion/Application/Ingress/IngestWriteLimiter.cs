namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Bounds how many direct submissions may be stored at once (spec 020 FR-013).
///
/// <para>
/// This is what keeps <c>429</c> meaningful. Until spec 020 it meant "the
/// ingest channel is full", and direct submissions no longer use that channel —
/// they store before answering. Without a replacement the endpoints would
/// silently become "queue behind the connection pool and eventually time out",
/// which is a worse answer to overload than an immediate refusal and would have
/// arrived by omission rather than by decision.
/// </para>
///
/// <para>
/// Sized to what the database can absorb concurrently, not to the old
/// 5 000-slot channel — that number measured burst absorption for the broker
/// path and never described these writes at all.
/// </para>
/// </summary>
public sealed class IngestWriteLimiter : IDisposable
{
    public const int DefaultConcurrency = 64;

    private readonly SemaphoreSlim slots;

    public IngestWriteLimiter() : this(DefaultConcurrency) { }

    public IngestWriteLimiter(int concurrency) => slots = new SemaphoreSlim(concurrency, concurrency);

    /// <summary>
    /// Takes a slot if one is free, without waiting. Refusing immediately is
    /// the point: a caller told to retry can, while a caller left waiting
    /// cannot tell a slow write from a lost one.
    /// </summary>
    public IngestWriteLease TryAcquire() =>
        slots.Wait(0) ? new IngestWriteLease(slots) : IngestWriteLease.Refused;

    public void Dispose() => slots.Dispose();
}

/// <summary>A held write slot, released when disposed.</summary>
public readonly struct IngestWriteLease : IDisposable
{
    public static IngestWriteLease Refused => default;

    private readonly SemaphoreSlim? slots;

    internal IngestWriteLease(SemaphoreSlim slots) => this.slots = slots;

    public bool Acquired => slots is not null;

    public void Dispose() => slots?.Release();
}
