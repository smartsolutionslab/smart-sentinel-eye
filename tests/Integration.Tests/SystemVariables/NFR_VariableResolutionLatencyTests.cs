using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 014 T031 — a baseline for the leg the reverse-index rewrite touches.
/// Measures <b>SystemVariables'</b> own part of it: a value change returning →
/// the overlay's resolved text carrying it.
///
/// <para>
/// <b>It does not close #749, and this file said it did until spec 106.</b> #749
/// is spec 007's T099 — an <i>Automation</i> measurement, over
/// <c>FabEventIngestedV1</c> consumed → action V1 published. Different context,
/// different span; nothing here enters <c>FabEventIngestedV1Handler</c>. Closing
/// #749 against this figure would have made the claim actively misleading. The
/// Automation part is measured by
/// <c>Automation/AcceptToDecideLatencyTests</c> — and even that is a proxy for
/// NFR-001's own span, so read its remarks before quoting either figure. No
/// assertion here changed: the measurement was always this one, only its label
/// was wrong.
/// </para>
///
/// <para>
/// Constitution §IV gives <c>event → overlay state</c> 200 ms of an 800 ms
/// budget. It is the product's load-bearing NFR and, before this, nothing
/// watched it: no test measured the leg at all, so a regression could only be
/// noticed on a kiosk.
/// </para>
///
/// <para>
/// <b>This must be taken against the global-keyed implementation, before T033
/// changes the key.</b> Measured afterwards it would compare the new code
/// against itself and pass trivially, which is exactly the failure mode the
/// phase gate exists to prevent. T039 re-runs it against the fab-keyed
/// implementation and records both figures.
/// </para>
///
/// <para>
/// What is measured: <c>PUT /system-variables/{name}/value</c> returning, then
/// polling <c>GET /system-variables/snapshot</c> until the resolved text
/// carries the new value. That spans the value write, the domain event, the
/// reverse-index lookup and the resolve — the whole of the leg that lives in
/// this context. It excludes the SignalR hop to the kiosk, which
/// <c>ResolvedTextReachesItsFabTests</c> covers separately.
/// </para>
///
/// <para>
/// That sentence used to name <c>OverlayPushIntegrationTests</c>, which covers
/// a different frame, from a different context, on a different trigger. The
/// resolved-text hop was covered by nothing at all, and a cross-reference that
/// made a gap look closed is part of why #2012 survived (spec 063 FR-008).
/// </para>
///
/// <para>
/// The assertion is deliberately loose relative to the observed figures, and
/// deliberately tight relative to constitution §IV. This runs on shared CI
/// against a cold stack, so a bound near the observed median would flake and
/// get deleted; the figure recorded in the output is the artefact that
/// matters, and <see cref="RegressionCeilingMs"/> is there to catch an
/// order-of-magnitude regression rather than to police the budget. See its own
/// remarks for the derivation, spec 171 (#2150), and a conflict spec 171's
/// re-measurement found between two of the derivation's own constraints.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class NFR_VariableResolutionLatencyTests(AspireFixture aspire) : IAsyncLifetime
{
    /// <summary>
    /// A regression ceiling anchored to observation, <b>not</b> constitution
    /// §IV's 200 ms leg 4 budget — the old name (<c>LegBudgetMs</c>) claimed
    /// otherwise, which is half of why this could not fail: at 800 ms the bound
    /// sat <i>above</i> the very budget it was named after, so a run breaching
    /// §IV by 3x still passed (spec 171 / #2150).
    ///
    /// <para>
    /// <b>Provenance.</b> Spec 014 recorded median 6 ms / worst 8 ms on its
    /// machine (<c>specs/014-.../tasks.md:238-239</c>), with a later local
    /// sample of <c>[8, 8, 10]</c>. Spec 171 re-measured on <i>this</i> machine,
    /// four times rather than the planned two, because the first two
    /// disagreed enough to warrant it:
    /// </para>
    ///
    /// <list type="table">
    /// <item><description>boot 1: median 19 ms, worst 23 ms, [9, 14, 19, 20, 23]</description></item>
    /// <item><description>boot 2: median 34 ms, worst 69 ms, [18, 26, 34, 34, 69]</description></item>
    /// <item><description>boot 3: median 19 ms, worst 34 ms, [11, 13, 19, 30, 34]</description></item>
    /// <item><description>boot 4: median 46 ms, worst 99 ms, [16, 28, 46, 66, 99]</description></item>
    /// </list>
    ///
    /// <para>
    /// This dev box, with the persistent Aspire stack it shares also running,
    /// is measurably noisier than spec 014's figures — samples climb across
    /// each run (e.g. boot 4's 16 → 28 → 46 → 66 → 99); the mechanism is not
    /// confirmed and is not needed to justify the number below, so it is not
    /// asserted as fact here.
    /// </para>
    ///
    /// <para>
    /// <b>The conflict, stated plainly.</b> The template's rule
    /// (<c>ResolvedTextReachesItsFabTests.cs:120-132</c>) is 10x the worst
    /// sample ever recorded. Taken literally against boot 4's 99 ms, that is
    /// ~1000 ms — <i>above</i> the 200 ms §IV budget, which recreates the exact
    /// defect this rename exists to remove. The two constraints — enough
    /// headroom above observed noise, and a ceiling that stays under the
    /// budget it guards — cannot both be satisfied once observed worst-case
    /// noise exceeds ~20 ms on this box, and nothing resolves that for you: the
    /// budget constraint wins, because a "regression ceiling" that sits above
    /// the leg it is meant to help guard is incoherent regardless of what the
    /// multiplier says.
    /// </para>
    ///
    /// <para>
    /// <b>Why 100 ms still holds despite that.</b> The assertion is on the
    /// <b>median</b> of 5, not the worst single sample — every boot's median
    /// above (19, 34, 19, 46 ms) clears 100 ms by at least 2.1x, worst boot
    /// included, so the noise this box actually produces does not threaten to
    /// flake it. And it stays below the §IV budget, which is the load-bearing
    /// half: at 100 ms a breach of the constitution's leg fails here first,
    /// where 800 ms sat above the thing it was named after.
    /// </para>
    /// </summary>
    private const int RegressionCeilingMs = 100;

    /// <summary>
    /// Kept at 3 even after #2201 corrected
    /// <see cref="OverlaySnapshotReadiness.WaitUntilResolvableAsync"/> to
    /// actually wait: the readiness wait exercises only <c>GET
    /// /system-variables/snapshot</c> — the read path. The measured loop below exercises a
    /// different path entirely — the version read, <c>PUT .../value</c>, the domain event,
    /// the outbox, and the resolve — so warmup round 0 remains that write-and-propagate
    /// path's first execution, absorbing first-call JIT, Wolverine handler resolution and EF
    /// plan compilation that would otherwise land inside the first measured sample. The two
    /// warm different paths; neither makes the other redundant. The single local observation
    /// to date, <c>[8, 8, 10]</c>, does not show round 0 as distinguishable from rounds 1-2;
    /// the warmups stay regardless, because the downside of being wrong is a corrupted
    /// latency figure, not because the data demands them.
    /// </summary>
    private const int WarmupRounds = 3;

    private const int MeasuredRounds = 5;

    public Task InitializeAsync() => aspire.ResetSystemVariablesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Value_change_reaches_the_resolved_overlay_text_within_the_leg_budget()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        string variableName = VariableRequests.UniqueName();
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name = variableName,
            type = "Number",
            initialValue = "0",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();

        Guid overlay = await OverlayRequests.PublishWithLabelAsync(
            overlays, $"Line 1: {{{{{variableName}}}}}", "Nfr");

        // The index is populated by an integration event, so the overlay is not
        // resolvable the instant publish returns. Wait for it before timing
        // anything, or the first measurement is really a measurement of
        // Wolverine's delivery of a different event.
        await OverlaySnapshotReadiness.WaitUntilResolvableAsync(variables, overlay, variableName);

        List<long> warmups = [];
        for (int round = 0; round < WarmupRounds; round++)
        {
            warmups.Add(await MeasureOneChangeAsync(variables, overlay, variableName, 1000 + round));
        }

        List<long> measured = [];
        for (int round = 0; round < MeasuredRounds; round++)
        {
            measured.Add(await MeasureOneChangeAsync(variables, overlay, variableName, 2000 + round));
        }

        measured.Sort();
        long median = measured[measured.Count / 2];
        long worst = measured[^1];

        // The artefact. Recorded in the test output so the figure survives the
        // run and T039 has something to compare against.
        Console.WriteLine(
            $"[NFR spec 014 T031] SystemVariables: value-change -> resolved overlay text, "
            + $"GLOBAL-KEYED baseline: "
            + $"median {median} ms, worst {worst} ms, samples [{string.Join(", ", measured)}] ms "
            + $"(constitution §IV leg 4 budget: 200 ms)");

        // #2201 US-2's observation: is round 0 of the write-and-propagate path markedly
        // slower than rounds 1-2? If so, WarmupRounds is demonstrably load-bearing rather
        // than a trim someone could remove now that the readiness wait is fixed.
        Console.WriteLine(
            $"[NFR spec 014 T031 warmup] write-and-propagate warmup samples "
            + $"[{string.Join(", ", warmups)}] ms — round 0 is that path's first execution");

        median.ShouldBeLessThan(RegressionCeilingMs);
    }

    /// <summary>
    /// One value change, timed from the write returning to the snapshot
    /// carrying it. Polls tightly rather than sleeping: a fixed delay would
    /// quantise every sample to the delay and measure the test, not the system.
    /// </summary>
    private static async Task<long> MeasureOneChangeAsync(
        HttpClient variables, Guid overlay, string variableName, int value)
    {
        HttpResponseMessage written = await VariableRequests.SetValueAsync(
            variables, variableName, value.ToString(CultureInfo.InvariantCulture));
        written.EnsureSuccessStatusCode();

        Stopwatch stopwatch = Stopwatch.StartNew();
        string expected = value.ToString(CultureInfo.InvariantCulture);

        while (stopwatch.ElapsedMilliseconds < 10_000)
        {
            string? resolved = await OverlaySnapshotReadiness.ResolvedTextAsync(variables, overlay);

            // A non-200 meant "not a match" before this method returned string.Empty for
            // it, and it must go on meaning exactly that now that it returns null (#2201) —
            // this poll's own semantics are unchanged, only the spelling of "absent" moved.
            if (resolved is not null && resolved.Contains(expected, StringComparison.Ordinal))
            {
                return stopwatch.ElapsedMilliseconds;
            }
        }

        throw new TimeoutException(
            $"Resolved text never carried '{expected}' for overlay {overlay} within 10 s.");
    }
}
