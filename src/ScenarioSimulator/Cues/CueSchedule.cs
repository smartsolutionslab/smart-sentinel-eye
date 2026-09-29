using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Cues;

/// <summary>Which (anchor, loop, index) triple a caller last emitted or skipped.</summary>
public readonly record struct CueCursor(DateTimeOffset Anchor, int Loop, int Index);

/// <summary>
/// What <see cref="CueSchedule.Next"/> decided for the candidate cue it found
/// (spec 289 plan.md §5.1).
/// </summary>
public abstract record CueDecision
{
    /// <summary>The candidate is due now, or up to <see cref="CueSchedule.LateTolerance"/> in the past.</summary>
    public sealed record Emit(CueDefinition Cue, int Loop, int Index, DateTimeOffset DueAt, CueCursor NextCursor) : CueDecision;

    /// <summary>The candidate is more than <see cref="CueSchedule.LateTolerance"/> in the past; it is not emitted.</summary>
    public sealed record Skip(CueDefinition Cue, int Loop, int Index, DateTimeOffset DueAt, TimeSpan Lateness, CueCursor NextCursor) : CueDecision;

    /// <summary>The candidate is not due yet; call <see cref="CueSchedule.Next"/> again after <see cref="Delay"/>.</summary>
    public sealed record Wait(TimeSpan Delay) : CueDecision;
}

/// <summary>
/// Pure scheduling arithmetic for a looping clip's cues (spec 289 FR-004,
/// plan.md §5.1). No I/O, no clock of its own — the caller supplies
/// <paramref name="now"/>.
///
/// <para>
/// Two paths, chosen by <paramref name="lastEmitted"/>: when it is
/// <c>null</c>, or its <see cref="CueCursor.Anchor"/> differs from
/// <paramref name="anchor"/> (a restart), the candidate is found fresh from
/// the clip's <b>current</b> phase — a jump to "where the clip is now", not
/// a replay of every loop since the dawn of time. Otherwise the candidate is
/// simply the next cue after <paramref name="lastEmitted"/> in sequence,
/// regardless of the clip's current phase — this is what makes lateness
/// observable.
/// </para>
/// </summary>
public static class CueSchedule
{
    public static readonly TimeSpan LateTolerance = TimeSpan.FromMilliseconds(250);

    public static CueDecision Next(DateTimeOffset anchor, DateTimeOffset now, ClipManifest manifest, CueCursor? lastEmitted)
    {
        Ensure.That(manifest).IsNotNull();

        (int loop, int index) = lastEmitted is { } cursor && cursor.Anchor == anchor
            ? Advance(cursor, manifest.Cues.Count)
            : FindCurrentPhase(anchor, now, manifest);

        CueDefinition cue = manifest.Cues[index];
        DateTimeOffset dueAt = anchor + TimeSpan.FromMilliseconds((long)loop * manifest.DurationMs + cue.AtMs);
        TimeSpan lateness = now - dueAt;
        CueCursor nextCursor = new(anchor, loop, index);

        if (lateness < TimeSpan.Zero)
        {
            return new CueDecision.Wait(dueAt - now);
        }

        return lateness <= LateTolerance
            ? new CueDecision.Emit(cue, loop, index, dueAt, nextCursor)
            : new CueDecision.Skip(cue, loop, index, dueAt, lateness, nextCursor);
    }

    /// <summary>Next cue after the cursor, wrapping to the next loop's first cue.</summary>
    private static (int Loop, int Index) Advance(CueCursor cursor, int cueCount)
    {
        int nextIndex = cursor.Index + 1;
        return nextIndex >= cueCount ? (cursor.Loop + 1, 0) : (cursor.Loop, nextIndex);
    }

    /// <summary>
    /// Where the clip is right now: the first cue not yet reached in this
    /// loop, or loop+1's first. The acceptance window is widened backward by
    /// <see cref="LateTolerance"/> — a cold-start wake-up that lands a few
    /// milliseconds <b>past</b> a cue's own <c>AtMs</c> (real timer jitter, an
    /// HTTP round trip every tick) must still find that cue as the candidate,
    /// not silently roll forward to its next loop's copy. The dueAt-vs-now
    /// comparison below is what actually decides Emit vs Skip; a cue found
    /// this way is, by construction, never more than <see cref="LateTolerance"/>
    /// late, so it always resolves to Emit.
    /// </summary>
    private static (int Loop, int Index) FindCurrentPhase(DateTimeOffset anchor, DateTimeOffset now, ClipManifest manifest)
    {
        TimeSpan elapsed = now - anchor;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        long elapsedMs = (long)elapsed.TotalMilliseconds;
        long durationMs = manifest.DurationMs;
        int loop = (int)(elapsedMs / durationMs);
        long offset = elapsedMs % durationMs;
        double threshold = offset - LateTolerance.TotalMilliseconds;

        IReadOnlyList<CueDefinition> cues = manifest.Cues;
        for (int index = 0; index < cues.Count; index++)
        {
            if (cues[index].AtMs >= threshold)
            {
                return (loop, index);
            }
        }

        return (loop + 1, 0);
    }
}
