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
    /// FR-001/FR-004 — a fact-scoped log capture for a tailed resource.
    ///
    /// <para>
    /// <b>Skeleton (phase 4a, spec 291 T001).</b> The <see cref="LogCapture"/>
    /// returned here is ring-backed: its <c>Lines</c>/<c>Contains</c> read
    /// <see cref="RecentLogs(string, int)"/> on demand — today's 400-line
    /// semantics, unchanged. It is not yet fed by <see cref="RecordLogLine"/>'s
    /// fan-out (that arrives in phase 4b, plan.md D2); this method exists only
    /// so <c>A_capture_keeps_a_line_the_tail_has_already_evicted</c> compiles
    /// and fails at the one assertion FR-001 is about, not anywhere else.
    /// </para>
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

        return new LogCapture(this, resourceName);
    }

    /// <summary>
    /// FR-003 — the one place a line is recorded into the 400-line ring,
    /// extracted from <see cref="TailResourceLogsAsync"/>'s per-line loop body
    /// (D2) so the ring and — from phase 4b — every active capture read the
    /// same recording rather than two copies that could disagree.
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
    /// <see cref="ConcurrentQueue{T}.Enqueue"/> and
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>
    /// cannot throw here; keep it that way.
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
    }
}
