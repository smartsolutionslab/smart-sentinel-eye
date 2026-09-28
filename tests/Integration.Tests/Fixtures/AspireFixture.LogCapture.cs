using System.Collections.Concurrent;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Spec 291 (#2656) — "the sweep that ate the sentinel". <see cref="RecentLogs"/>'s
/// 400-line ring buffer is a <em>tail</em>, not a record: at 40 provisioned
/// streams a single <c>StreamHealthWatcher</c> sweep writes exactly 400 lines
/// in under 100 ms (spec 291 §1.2), so a fact whose two log reads straddle a
/// sweep can lose its own sentinel and transition record between them. This
/// partial adds a fact-scoped alternative that does not share that ceiling.
/// </summary>
public sealed partial class AspireFixture
{
    /// <summary>
    /// Active captures per resource (plan.md D1's exact shape). Populated by
    /// <see cref="CaptureLogs"/>, drained by <see cref="UnregisterCapture"/>,
    /// fanned out to by <see cref="RecordLogLine"/>. The inner dictionary is a
    /// set — the <see langword="byte"/> value is unused.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<LogCapture, byte>> activeCaptures =
        new(StringComparer.Ordinal);

    /// <summary>
    /// FR-001/FR-004 — a fact-scoped log capture for a tailed resource,
    /// registered so <see cref="RecordLogLine"/>'s fan-out (D2) feeds it every
    /// line the ring also receives, from now until <see cref="LogCapture.Dispose"/>.
    /// </summary>
    public LogCapture CaptureLogs(string resourceName)
    {
        Ensure.That(resourceName).IsNotNull().IsNotNullOrWhiteSpace();

        if (!_logTails.ContainsKey(resourceName))
        {
            throw new InvalidOperationException(
                $"'{resourceName}' is not tailed — add it to AspireFixture.TailedResources before "
                + "capturing its logs.");
        }

        var capture = new LogCapture(this, resourceName);
        ConcurrentDictionary<LogCapture, byte> captures =
            activeCaptures.GetOrAdd(resourceName, _ => new ConcurrentDictionary<LogCapture, byte>());
        captures.TryAdd(capture, 0);

        return capture;
    }

    /// <summary>
    /// Removes a disposed capture from the active-capture registry so it no
    /// longer receives lines. Called only from <see cref="LogCapture.Dispose"/>,
    /// which guards against calling this more than once per capture.
    /// </summary>
    internal void UnregisterCapture(string resourceName, LogCapture capture)
    {
        if (activeCaptures.TryGetValue(resourceName, out ConcurrentDictionary<LogCapture, byte>? captures))
        {
            captures.TryRemove(capture, out _);
        }
    }

    /// <summary>
    /// FR-003 — the one place a line is recorded, extracted from
    /// <see cref="TailResourceLogsAsync"/>'s per-line loop body (D2) so the
    /// ring and every active capture read the same recording rather than two
    /// copies that could disagree. Feeds the 400-line ring first, then fans
    /// the same line out to every capture currently open on this resource.
    ///
    /// <para>
    /// <c>internal</c>: it is also the seam spec 291 SC-1/SC-2 use to force
    /// eviction deterministically from a test, by enqueuing synthetic lines
    /// without a real resource writing them.
    /// </para>
    ///
    /// <para>
    /// Must not throw — <see cref="TailResourceLogsAsync"/>'s loop calls this
    /// once per delivered line, and an exception here is caught by that
    /// method's own handler, recorded as a tail fault and re-subscribed,
    /// dropping lines for every test until the re-subscribe completes.
    /// <see cref="ConcurrentQueue{T}.Enqueue"/>,
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>
    /// and enumerating a <see cref="ConcurrentDictionary{TKey,TValue}"/>'s
    /// <c>Keys</c> while it is written concurrently cannot throw here; keep it
    /// that way.
    /// </para>
    /// </summary>
    internal void RecordLogLine(string resourceName, string content)
    {
        ConcurrentQueue<string> tail = _logTails.GetOrAdd(resourceName, _ => new ConcurrentQueue<string>());
        tail.Enqueue(content);
        while (tail.Count > 400)
        {
            tail.TryDequeue(out _);
        }

        if (activeCaptures.TryGetValue(resourceName, out ConcurrentDictionary<LogCapture, byte>? captures))
        {
            foreach (LogCapture capture in captures.Keys)
            {
                capture.Record(content);
            }
        }
    }
}
