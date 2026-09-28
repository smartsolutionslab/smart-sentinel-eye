namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// Spec 291 (#2656) — a fact-scoped record of every log line a tailed
/// resource emitted between this capture's creation and its disposal,
/// unbounded by design because it lives for one fact (FR-001).
///
/// <para>
/// <b>Skeleton (phase 4a).</b> <see cref="Lines"/> and <see cref="Contains"/>
/// read <see cref="AspireFixture.RecentLogs(string, int)"/> on demand — the
/// same 400-line tail every other caller sees, so a line the ring has already
/// evicted is invisible here too. That is deliberate: FR-001 is declared by
/// this shape, not yet built by it. Phase 4b (plan.md D1/D2) replaces the
/// backing store with an unbounded queue fed by
/// <see cref="AspireFixture.RecordLogLine"/>'s fan-out; until then, this is
/// the exact property <c>A_capture_keeps_a_line_the_tail_has_already_evicted</c>
/// reds against.
/// </para>
/// </summary>
public sealed class LogCapture : IDisposable
{
    private readonly AspireFixture fixture;
    private readonly string resourceName;

    internal LogCapture(AspireFixture fixture, string resourceName)
    {
        this.fixture = fixture;
        this.resourceName = resourceName;
    }

    /// <summary>
    /// A snapshot of the lines this capture has retained, in arrival order.
    /// Skeleton: computed on demand from the 400-line ring (see the class
    /// doc); unbounded storage arrives in phase 4b.
    /// </summary>
    public IReadOnlyList<string> Lines =>
        fixture.RecentLogs(resourceName, lines: 400)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Ordinal substring search over <see cref="Lines"/>.</summary>
    public bool Contains(string marker) =>
        Lines.Any(line => line.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// Skeleton: nothing to release yet — <see cref="Lines"/> above owns no
    /// subscription of its own; it reads <see cref="AspireFixture.RecentLogs"/>
    /// fresh every time. Phase 4b's real capture unregisters itself from the
    /// fixture's active-capture registry here.
    /// </summary>
    public void Dispose()
    {
    }
}
