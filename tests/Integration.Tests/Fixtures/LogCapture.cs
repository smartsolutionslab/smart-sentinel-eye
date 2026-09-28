using System.Collections.Concurrent;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Spec 291 (#2656) — a fact-scoped record of every log line a tailed
/// resource emitted between this capture's creation and its disposal,
/// unbounded by design because it lives for one fact (FR-001).
///
/// <para>
/// Backed by its own <see cref="ConcurrentQueue{T}"/>, fed by
/// <see cref="AspireFixture.RecordLogLine"/>'s fan-out — the same call that
/// feeds the 400-line ring — so a line the ring has already evicted is still
/// readable here. <see cref="Lines"/> and <see cref="Contains"/> read only
/// this queue, independent of <see cref="AspireFixture.RecentLogs(string, int)"/>.
/// </para>
/// </summary>
public sealed class LogCapture : IDisposable
{
    private readonly ConcurrentQueue<string> lines = new();
    private readonly AspireFixture fixture;
    private readonly string resourceName;
    private int disposed;

    internal LogCapture(AspireFixture fixture, string resourceName)
    {
        this.fixture = fixture;
        this.resourceName = resourceName;
    }

    /// <summary>A snapshot of the lines this capture has retained, in arrival order.</summary>
    public IReadOnlyList<string> Lines => lines.ToArray();

    /// <summary>Ordinal substring search over <see cref="Lines"/>.</summary>
    public bool Contains(string marker) =>
        Lines.Any(line => line.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// Appends a line in arrival order. Called only from
    /// <see cref="AspireFixture.RecordLogLine"/>'s fan-out, which must not
    /// throw — <see cref="ConcurrentQueue{T}.Enqueue"/> cannot, so neither
    /// can this.
    /// </summary>
    internal void Record(string line) => lines.Enqueue(line);

    /// <summary>
    /// Unregisters this capture from the fixture's active-capture registry so
    /// it stops receiving lines and is no longer enumerated by
    /// <see cref="AspireFixture.RecordLogLine"/>. Idempotent — safe to call
    /// more than once.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            fixture.UnregisterCapture(resourceName, this);
        }
    }
}
