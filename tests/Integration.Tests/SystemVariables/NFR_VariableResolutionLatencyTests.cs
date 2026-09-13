using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
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
/// The assertion is deliberately loose. This runs on shared CI against a cold
/// stack, so a tight bound would flake and get deleted; the figure recorded in
/// the output is the artefact that matters, and <see cref="LegBudgetMs"/> is
/// there to catch an order-of-magnitude regression rather than to police the
/// budget.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class NFR_VariableResolutionLatencyTests(AspireFixture aspire) : IAsyncLifetime
{
    /// <summary>
    /// Constitution §IV leg 4 is 200 ms. Asserted at 4x to survive CI jitter
    /// and a cold JIT — see the class remarks on why this is not the budget.
    /// </summary>
    private const int LegBudgetMs = 800;

    /// <summary>
    /// Kept at 3 even after #2201 corrected <see cref="WaitUntilResolvableAsync"/> to
    /// actually wait: the readiness wait exercises only <c>GET
    /// /system-variables/snapshot</c> — the read path. The measured loop below exercises a
    /// different path entirely — the version read, <c>PUT .../value</c>, the domain event,
    /// the outbox, and the resolve — so warmup round 0 remains that write-and-propagate
    /// path's first execution, absorbing first-call JIT, Wolverine handler resolution and EF
    /// plan compilation that would otherwise land inside the first measured sample. The two
    /// warm different paths; neither makes the other redundant. See the second
    /// <c>Console.WriteLine</c> below for the observation this reasoning predicts.
    /// </summary>
    private const int WarmupRounds = 3;

    private const int MeasuredRounds = 5;

    /// <summary>Poll interval for <see cref="WaitUntilResolvableAsync"/> — the loop is now
    /// real (#2201), and an undelayed one would hot-spin a core against the API.</summary>
    private const int PollIntervalMs = 200;

    public Task InitializeAsync() => aspire.ResetSystemVariablesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Value_change_reaches_the_resolved_overlay_text_within_the_leg_budget()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        string variableName = UniqueVariableName();
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name = variableName,
            type = "Number",
            initialValue = "0",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();

        Guid overlay = await PublishOverlayReferencingAsync(overlays, variableName);

        // The index is populated by an integration event, so the overlay is not
        // resolvable the instant publish returns. Wait for it before timing
        // anything, or the first measurement is really a measurement of
        // Wolverine's delivery of a different event.
        await WaitUntilResolvableAsync(variables, overlay, variableName);

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

        median.ShouldBeLessThan(LegBudgetMs);
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
            string? resolved = await ResolvedTextAsync(variables, overlay);

            // A non-200 meant "not a match" before this method returned string.Empty for
            // it, and it must go on meaning exactly that now that it returns null (#2201) —
            // this poll's own semantics are unchanged, only ResolvedTextAsync's spelling of
            // "absent" moved.
            if (resolved is not null && resolved.Contains(expected, StringComparison.Ordinal))
            {
                return stopwatch.ElapsedMilliseconds;
            }
        }

        throw new TimeoutException(
            $"Resolved text never carried '{expected}' for overlay {overlay} within 10 s.");
    }

    /// <summary>
    /// <c>internal</c> rather than <c>private</c> so <c>OverlaySnapshotReadinessTests</c>
    /// (#2201) can drive it directly against a scripted handler. Both this and
    /// <see cref="ResolvedTextAsync"/> are invisible to every caller but that one and this
    /// file's own measured test, which passes no <c>ceilingMs</c> and keeps its 30 s
    /// ceiling unchanged. The widening is deleted once both bodies move into a shared
    /// fixture.
    ///
    /// <para>
    /// Copied from <c>TwoPlaceholdersInOneLabelTests.WaitUntilResolvableAsync</c>'s shape
    /// (#2201): readiness requires both a 200 <b>and</b> the literal placeholder gone —
    /// neither alone — and the poll delays between attempts rather than spinning.
    /// </para>
    /// </summary>
    internal static async Task WaitUntilResolvableAsync(
        HttpClient variables, Guid overlay, string variableName, int ceilingMs = 30_000)
    {
        string literal = $"{{{{{variableName}}}}}";
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? resolved = null;

        while (stopwatch.ElapsedMilliseconds < ceilingMs)
        {
            resolved = await ResolvedTextAsync(variables, overlay);

            // Until the index knows the overlay, the snapshot renders the literal
            // placeholder. Its disappearance, on top of an actual 200, is the readiness
            // signal — neither half alone is enough (#2201).
            if (resolved is not null && !resolved.Contains(literal, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(PollIntervalMs);
        }

        throw new TimeoutException(
            $"Overlay {overlay} never resolved '{variableName}' within {ceilingMs} ms; "
            + $"the last snapshot was "
            + $"{(resolved is null ? "not a 200" : $"a 200 carrying '{resolved}'")}.");
    }

    /// <summary>
    /// The resolved text, or <c>null</c> when the snapshot did not answer 200 — the two are
    /// different states and the readiness wait must tell them apart (#2201).
    /// <c>string.Empty</c> could not: <c>string.Empty.Contains(anything non-empty)</c>
    /// answers <c>false</c>, the same answer a fully resolved label gives.
    /// </summary>
    internal static async Task<string?> ResolvedTextAsync(HttpClient variables, Guid overlay)
    {
        HttpResponseMessage snapshot = await variables.GetAsync(
            $"/system-variables/snapshot?overlayIdentifier={overlay}");
        if (!snapshot.IsSuccessStatusCode)
        {
            return null;
        }

        JsonElement payload = await snapshot.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("resolvedText").GetString() ?? string.Empty;
    }

    private static async Task<Guid> PublishOverlayReferencingAsync(HttpClient overlays, string variableName)
    {
        HttpResponseMessage created = await overlays.PostAsJsonAsync("/overlays", new
        {
            name = $"Nfr-{Guid.NewGuid():N}"[..16],
            label = new
            {
                text = $"Line 1: {{{{{variableName}}}}}",
                normalizedX = 0.5m,
                normalizedY = 0.05m,
                normalizedWidth = 0.3m,
                normalizedHeight = 0.08m,
                fontSizePx = 48,
            },
        });
        created.EnsureSuccessStatusCode();

        Guid overlay = await created.Content.ReadFromJsonAsync<Guid>();
        (await OverlayRequests.PostAsync(overlays, overlay, "revisions/1/publish")).EnsureSuccessStatusCode();

        return overlay;
    }

    private static string UniqueVariableName() => $"v{Guid.NewGuid():N}"[..12];
}
