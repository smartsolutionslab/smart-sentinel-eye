using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-B, T-B01 (ADR-0144 red). Pure scheduling arithmetic for
/// <c>CueSchedule.Next</c> (plan.md §5.1) — no I/O, no clock, no hosted
/// service. <c>CueSchedule</c> does not exist yet; this class is the target
/// shape the engineer implements against (plan §4, T-B11).
///
/// <para>
/// <b>Contract, precisely, because this is the brief:</b>
/// <c>Next(anchor, now, manifest, lastEmitted)</c> picks exactly one
/// candidate cue and classifies it:
/// </para>
/// <list type="bullet">
/// <item><description>
/// If <paramref name="lastEmitted"/> is <c>null</c>, or its
/// <c>Anchor</c> differs from <paramref name="anchor"/> (a restart), the
/// candidate is found fresh from the clip's <b>current</b> phase: with
/// <c>elapsed = now - anchor</c> (clamped to zero when negative — clock
/// skew, a future anchor), <c>loop = floor(elapsed / D)</c>,
/// <c>offset = elapsed mod D</c>, the candidate is the first cue in that
/// loop with <c>AtMs &gt;= offset</c>, or, if none, cue 0 of <c>loop + 1</c>.
/// This is a jump to "where the clip is now", not a replay of every loop
/// since the dawn of time.
/// </description></item>
/// <item><description>
/// Otherwise the candidate is simply the next cue after
/// <c>lastEmitted</c> in sequence (index + 1, wrapping to the next loop
/// after the last cue) — regardless of where the clip's phase currently
/// is. This is what makes lateness observable: a candidate identified by
/// one call can be overtaken by the clock before the next call notices it.
/// </description></item>
/// <item><description>
/// The candidate's <c>dueAt = anchor + loop * DurationMs + cue.AtMs</c> is
/// then compared with <paramref name="now"/>: due in the future →
/// <c>Wait</c>(time remaining); due now or up to
/// <c>CueSchedule.LateTolerance</c> (250 ms) in the past → <c>Emit</c>;
/// more than 250 ms in the past → <c>Skip</c> (not emitted — FR-004's
/// point: a late highlight is worse than none). Both <c>Emit</c> and
/// <c>Skip</c> carry the <c>NextCursor</c> the caller must pass back in as
/// the next call's <paramref name="lastEmitted"/>.
/// </description></item>
/// </list>
/// </summary>
public sealed class CueScheduleTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_cue_of_loop_zero_is_picked_when_nothing_has_been_emitted_yet()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000), Cue(12_000));

        CueDecision decision = CueSchedule.Next(Anchor, Anchor, manifest, lastEmitted: null);

        CueDecision.Wait wait = decision.ShouldBeOfType<CueDecision.Wait>();
        wait.Delay.ShouldBe(TimeSpan.FromMilliseconds(4_000));
    }

    [Fact]
    public void A_cue_due_now_is_emitted()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000), Cue(12_000));
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(4_000);

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted: null);

        CueDecision.Emit emit = decision.ShouldBeOfType<CueDecision.Emit>();
        emit.Loop.ShouldBe(0);
        emit.Index.ShouldBe(0);
        emit.Cue.AtMs.ShouldBe(4_000);
        emit.DueAt.ShouldBe(Anchor + TimeSpan.FromMilliseconds(4_000));
        emit.NextCursor.ShouldBe(new CueCursor(Anchor, 0, 0));
    }

    [Fact]
    public void After_the_last_cue_of_a_loop_the_schedule_wraps_to_the_first_cue_of_the_next_loop()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000), Cue(12_000));
        CueCursor lastEmitted = new(Anchor, Loop: 0, Index: 1); // the 12_000ms cue, already emitted
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(20_000 + 4_000); // loop 1's first cue is due

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted);

        CueDecision.Emit emit = decision.ShouldBeOfType<CueDecision.Emit>();
        emit.Loop.ShouldBe(1);
        emit.Index.ShouldBe(0);
        emit.Cue.AtMs.ShouldBe(4_000);
        emit.DueAt.ShouldBe(Anchor + TimeSpan.FromMilliseconds(20_000 + 4_000));
    }

    [Fact]
    public void A_cue_already_recorded_as_emitted_is_never_offered_again_in_the_same_loop()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000), Cue(12_000));
        // The 4_000ms cue (index 0) was already emitted; the schedule must move on
        // to index 1, not re-offer index 0 even though "now" has looped back to it
        // logically (it hasn't — this call only ever advances forward from the cursor).
        CueCursor lastEmitted = new(Anchor, Loop: 0, Index: 0);
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(12_000);

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted);

        CueDecision.Emit emit = decision.ShouldBeOfType<CueDecision.Emit>();
        emit.Loop.ShouldBe(0);
        emit.Index.ShouldBe(1);
    }

    [Fact]
    public void A_changed_anchor_is_treated_as_a_restart_and_the_stale_cursor_is_ignored()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000), Cue(12_000));
        DateTimeOffset oldAnchor = Anchor - TimeSpan.FromMinutes(5);
        CueCursor staleCursor = new(oldAnchor, Loop: 3, Index: 1);

        // The path restarted: readyTime moved to `Anchor`. `now` is exactly the new
        // anchor, so the fresh search must start at loop 0 / the first cue, not
        // wherever the stale (old-anchor) cursor left off.
        CueDecision decision = CueSchedule.Next(Anchor, Anchor, manifest, staleCursor);

        CueDecision.Wait wait = decision.ShouldBeOfType<CueDecision.Wait>();
        wait.Delay.ShouldBe(TimeSpan.FromMilliseconds(4_000));
    }

    [Fact]
    public void A_cue_more_than_250ms_late_is_skipped_rather_than_emitted()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000), Cue(12_000));
        CueCursor lastEmitted = new(Anchor, Loop: 0, Index: 0); // index 1 (12_000ms) is next
        // The caller only got around to asking 400ms after the cue's own due time —
        // a slow HTTP round trip to camera-sim, or a delayed wake-up.
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(12_400);

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted);

        CueDecision.Skip skip = decision.ShouldBeOfType<CueDecision.Skip>();
        skip.Loop.ShouldBe(0);
        skip.Index.ShouldBe(1);
        skip.Cue.AtMs.ShouldBe(12_000);
        skip.Lateness.ShouldBe(TimeSpan.FromMilliseconds(400));
        skip.NextCursor.ShouldBe(new CueCursor(Anchor, 0, 1));
    }

    [Fact]
    public void A_cue_exactly_at_the_250ms_tolerance_boundary_is_still_emitted()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000));
        CueCursor lastEmitted = new(Anchor, Loop: -1, Index: 0); // forces Advance() to (loop 0, index 0)
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(4_000) + CueSchedule.LateTolerance;

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted);

        decision.ShouldBeOfType<CueDecision.Emit>();
    }

    /// <summary>
    /// Backend-reviewer finding B1 (blocker). The cold-start search (used
    /// whenever <c>lastEmitted</c> is <c>null</c> or stale — i.e. every tick
    /// after a <see cref="CueDecision.Wait"/>,
    /// since <c>ClipCueHostedService</c> never records a cursor for a
    /// <c>Wait</c>) only accepts a candidate with <c>cue.AtMs &gt;= offset</c>
    /// in whole milliseconds. A wake-up landing even 1ms after a cue's own
    /// <c>AtMs</c> — inevitable with real timer jitter and an HTTP round trip
    /// to camera-sim on every tick — makes that candidate fail the test and
    /// silently jump to the <b>next loop's</b> copy of the same cue: no
    /// <see cref="CueDecision.Skip"/>, no log, nothing. On a real clock this
    /// cue essentially never fires. A wake-up a few ms late must still land
    /// within <see cref="CueSchedule.LateTolerance"/> of <b>this loop's</b>
    /// cue and <see cref="CueDecision.Emit"/> it.
    /// </summary>
    [Fact]
    public void A_cold_start_wake_up_a_few_milliseconds_late_still_emits_this_loops_cue_rather_than_skipping_a_whole_loop()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000));
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(4_000) + TimeSpan.FromMilliseconds(5);

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted: null);

        CueDecision.Emit emit = decision.ShouldBeOfType<CueDecision.Emit>(
            "a 5ms-late cold-start wake-up must still catch this loop's cue (well inside the 250ms "
            + "tolerance), not silently roll forward to the next loop's copy of it");
        emit.Loop.ShouldBe(0);
        emit.Index.ShouldBe(0);
    }

    [Fact]
    public void A_future_anchor_from_clock_skew_makes_the_schedule_wait_rather_than_throw()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000));
        DateTimeOffset now = Anchor - TimeSpan.FromSeconds(2); // camera-sim's readyTime is "ahead" of our clock

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted: null);

        CueDecision.Wait wait = decision.ShouldBeOfType<CueDecision.Wait>();
        wait.Delay.ShouldBe(TimeSpan.FromSeconds(2) + TimeSpan.FromMilliseconds(4_000));
    }

    [Fact]
    public void A_manifest_with_a_single_cue_still_loops()
    {
        ClipManifest manifest = Manifest(20_000, Cue(4_000));
        CueCursor lastEmitted = new(Anchor, Loop: 0, Index: 0);
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(20_000 + 4_000);

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted);

        CueDecision.Emit emit = decision.ShouldBeOfType<CueDecision.Emit>();
        emit.Loop.ShouldBe(1);
        emit.Index.ShouldBe(0);
    }

    [Fact]
    public void A_cue_at_AtMs_zero_lands_exactly_on_the_loop_boundary()
    {
        ClipManifest manifest = Manifest(20_000, Cue(0), Cue(12_000));
        CueCursor lastEmitted = new(Anchor, Loop: 0, Index: 1); // 12_000ms cue already emitted
        DateTimeOffset now = Anchor + TimeSpan.FromMilliseconds(20_000); // exactly loop 1's start

        CueDecision decision = CueSchedule.Next(Anchor, now, manifest, lastEmitted);

        CueDecision.Emit emit = decision.ShouldBeOfType<CueDecision.Emit>();
        emit.Loop.ShouldBe(1);
        emit.Index.ShouldBe(0);
        emit.DueAt.ShouldBe(Anchor + TimeSpan.FromMilliseconds(20_000));
    }

    private static ClipManifest Manifest(int durationMs, params CueDefinition[] cues) =>
        new("clip.mp4", durationMs, cues);

    private static CueDefinition Cue(int atMs) =>
        new(AtMs: atMs, Class: "person", Confidence: 0.9);
}
