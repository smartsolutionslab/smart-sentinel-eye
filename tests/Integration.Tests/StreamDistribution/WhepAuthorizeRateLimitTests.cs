using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 208 (#2284): <c>POST /streams/authorize</c> is MediaMTX's anonymous
/// external-auth hook and today accepts requests without limit, so an
/// unauthenticated caller with nothing but network reach can burn CPU on RSA
/// signature verification forever. These tests observe the ceiling FR-001
/// through FR-007 describe.
///
/// <para>
/// <b>Red, deliberately, for T003/T005/T006's declaration case.</b> T001+T002
/// (commit <c>df311774</c>) landed only the configuration — <c>appsettings.json</c>'s
/// production default and <c>AppHost.cs</c>'s <c>isE2ETests</c> override — and
/// nothing reads it yet. No policy is registered, so every request today is
/// answered on its own merits, however many are sent.
/// </para>
///
/// <para>
/// <b>The test-mode ceiling, not the production one.</b> <c>AppHost.cs</c>
/// overrides <c>WhepAuthorizeRateLimiting__PermitLimit=50</c> and
/// <c>:Window=00:00:10</c> for the integration lane (<c>isE2ETests</c>), because
/// the production ceiling (2000/min) cannot be exhausted from a test host without
/// poisoning every other test on this shared fixture's one address for the rest
/// of that minute (plan.md §Test-mode ceiling). <c>PermitLimit</c> was raised
/// from its original <c>20</c> to <c>30</c>, then to <c>50</c> (spec 208 review
/// S8), because this same collapsed partition is shared by more than just this
/// class's own exhaustion technique — see <c>AppHost.cs</c>'s comment above the
/// override for the full accounting of every consumer. It is written here as
/// the literal <c>50</c> rather than read live from the running service's
/// configuration: no existing test helper resolves a downstream service's bound
/// <c>IConfiguration</c> from the AppHost's own DI container
/// (<c>aspire.App.Services</c> is the orchestrator's container, not
/// stream-distribution's), and unlike a production security parameter this is
/// the test's <i>own</i> fixture configuration — the number this file was told
/// to expect, not a value whose drift would go unnoticed.
/// </para>
///
/// <para>
/// <b>Every test that exhausts the window waits for it to reopen before
/// returning</b> — condition-based, polling for the first admitted response,
/// never a fixed sleep in place of that condition (constitution §Testing) —
/// because <c>AspireCollection</c> serialises every test class here against one
/// running stack; a test that returns with the window still exhausted would
/// poison whichever test runs next. <b>Corrected, then replaced (spec 208
/// review S10, spec 251/#2537):</b> <see cref="WaitUntilAdmittedAgainAsync"/>
/// used to hold for one additional full <c>Window</c> after an observed
/// admission before returning, on the theory that a delay of exactly
/// <c>Window</c> reliably lands the next fact just past the following
/// boundary. It does not: the limiter's boundary is heartbeat-driven (one
/// heartbeat every 100 ms replenishes the window only once
/// <c>now - lastReplenishment >= Window</c>), so that fixed delay could drift
/// past the boundary under heartbeat lateness, handing the next fact a
/// window that already held a spent permit. Every limiter-counting fact now
/// observes its own starting boundary directly, through
/// <see cref="BeginCountingAsync"/>, rather than inheriting one from a
/// recovery delay; see that method's own doc comment for the mechanism.
/// </para>
///
/// <para>
/// <b>Spec 251 (#2537).</b> The premise every fact's arithmetic rests on —
/// "this fact begins in a fresh window with all <c>PermitLimit</c> permits"
/// — is now observed rather than guessed: every limiter-counting fact calls
/// the alignment seam, <see cref="BeginCountingAsync"/>, which sends until
/// refused and polls until admitted again, proving a reset happened between
/// the two before the fact spends a single counted request. Recovery moved
/// to <see cref="DisposeAsync"/>, so it runs after a failing fact too and no
/// longer poisons whichever fact in <c>AspireCollection</c> runs next (spec
/// 251 §1.1, run 35845632059's <c>Repeated…</c> cascade after a failed
/// <c>A_throttled…</c>).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WhepAuthorizeRateLimitTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string StreamDistributionResource = "stream-distribution";

    /// <summary>
    /// Matches <c>AppHost.cs</c>'s <c>isE2ETests</c> override, not the
    /// production default in <c>appsettings.json</c> — see the class doc for
    /// why this is a literal rather than a live config read. Raised from the
    /// original <c>20</c> to <c>30</c> to give
    /// <c>WhepHandshakeLatencyTests</c>' own 21-call pattern (1 warm-up + 20
    /// measured, spec 002/#2149) real headroom on this same shared-address
    /// partition (#2284 phase-5 verification.md §2.1), then to <c>50</c>
    /// (spec 208 review S8): 30 left only 3 requests of margin once
    /// <c>WhepAuthIntegrationTests</c>' own 6 authorize POSTs on this same
    /// partition were accounted for (21 + 6 = 27 of 30) — tight enough that
    /// the next added authorize-related test could reintroduce the exact bug
    /// this ceiling exists to avoid. See <c>AppHost.cs</c>'s comment above
    /// its override for the full three-consumer accounting
    /// (<c>WhepHandshakeLatencyTests</c>, <c>WhepAuthIntegrationTests</c>,
    /// and this class's own <see cref="ExhaustWindowAsync"/>).
    /// </summary>
    private const int PermitLimit = 50;

    /// <summary>Matches <c>AppHost.cs</c>'s <c>isE2ETests</c> override.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Spec 251 FR-001: the poll cadence for observing a window boundary —
    /// short enough that the boundary is known to lie within it. Used by
    /// <see cref="BeginCountingAsync"/> (from phase 4b) and by
    /// <see cref="PollUntilAdmittedAsync"/>, which both this class's own
    /// adversarial-phase fact and (from 4b) <see cref="BeginCountingAsync"/>
    /// share — deliberately not <see cref="PollInterval"/>, which is coarser
    /// and only ever used for log-tail polling.
    /// </summary>
    private static readonly TimeSpan AlignmentPollInterval = TimeSpan.FromMilliseconds(50);

    // Comfortably longer than Window so a slow CI runner does not read "never
    // recovers" where the truth is "recovers a little late".
    private static readonly TimeSpan AdmissionRecoveryTimeout = Window + TimeSpan.FromSeconds(20);

    // How long to keep polling the log tail for a marker that this test
    // expects never to arrive, before concluding it really did not. Long
    // enough that a slow-but-real delivery is not mistaken for an absence
    // (LogTailDeliversIntegrationTests observes tail delivery well within this).
    private static readonly TimeSpan LogAbsenceGraceWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// US2 (FR-009): no fixed message text exists to search for yet — T011's
    /// <c>OnRejected</c> callback is a separate, not-yet-written task, so
    /// there is no literal string this repository has committed to. A log
    /// line counts as the rate limiter's own throttle-transition record if it
    /// carries either the limiter's one stable identifier already spelled in
    /// <c>Program.cs</c> (the policy name, <c>"whep-authorize"</c>) or the
    /// word FR-009 itself uses for the state being entered ("throttled").
    /// Verified before writing these tests: neither substring appears in this
    /// service's runtime log output today — both are source-only (comments,
    /// and a policy-name literal that Program.cs never logs) — so a match is
    /// a positive signal for this control specifically, not an ambient false
    /// hit.
    /// </summary>
    private static readonly string[] ThrottleTransitionMarkers = ["whep-authorize", "throttl"];

    /// <summary>
    /// xUnit 2.9.3 lifecycle hook (spec 251 FR-003) — no per-fact setup is
    /// needed, only the paired <see cref="DisposeAsync"/>.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Spec 251 FR-003: recovery moves here from each fact's own trailing
    /// call so it also runs when a fact fails — today a failed fact skips
    /// its own recovery and poisons whichever fact in
    /// <see cref="AspireCollection"/> runs next (spec 251 §1.1, run
    /// 35845632059's <c>Repeated…</c> cascade after a failed
    /// <c>A_throttled…</c>). Phase 4a: still today's
    /// <see cref="WaitUntilAdmittedAgainAsync"/>, delay included, so this
    /// phase changes only *where* recovery runs, not what it does.
    /// </summary>
    public Task DisposeAsync() => WaitUntilAdmittedAgainAsync();

    /// <summary>
    /// Spec 251 FR-001 — the alignment seam. Every limiter-counting fact
    /// calls this once, before sending any request whose outcome it counts,
    /// and works from the budget it returns rather than <see cref="PermitLimit"/>
    /// directly.
    /// </summary>
    /// <remarks>
    /// Sends <see cref="NoTokenBody"/> requests until one is refused with
    /// <c>429</c> (bounded — fails "the window never refused" if
    /// <see cref="PermitLimit"/> + 1 sends never see one), then shares
    /// <see cref="PollUntilAdmittedAsync"/>'s condition to observe the window
    /// reopening: poll every <see cref="AlignmentPollInterval"/> until a
    /// non-<c>429</c> answer, bounded by <see cref="AdmissionRecoveryTimeout"/>.
    /// A refusal followed by an admission proves a reset happened between
    /// them; at a 50 ms cadence the boundary is known to within roughly that
    /// poll interval plus one round trip, and the *next* boundary is a full
    /// <see cref="Window"/> after it (spec 251 §1.2: windows only ever
    /// lengthen, by heartbeat drift, never shorten). The admitted probe
    /// itself spent one permit in the fresh window, so the fact's budget is
    /// <c>PermitLimit - 1</c>.
    /// </remarks>
    private async Task<int> BeginCountingAsync()
    {
        bool refused = false;

        for (int attempt = 0; attempt < PermitLimit + 1; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(NoTokenBody(NewPath()));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused = true;
                break;
            }
        }

        refused.ShouldBeTrue(
            $"the window never refused after {PermitLimit + 1} requests — is another limiter "
            + "configuration live?");

        await PollUntilAdmittedAsync();

        return PermitLimit - 1;
    }

    /// <summary>
    /// Spec 271 §10 — the alignment seam's own transition-log side effect,
    /// for the two facts that measure that exact signal.
    /// </summary>
    /// <remarks>
    /// <see cref="BeginCountingAsync"/>'s forced refusal is itself a genuine
    /// throttle transition, debounced by <c>Program.cs</c>'s
    /// <c>IsNewWhepAuthorizeThrottleTransition</c> (an absolute TTL equal to
    /// <see cref="Window"/>, keyed only on partition). Under the pre-#2537
    /// protocol, the prior fact's own trailing <c>Task.Delay(Window)</c>
    /// happened to leave that debounce cold by the time the next fact
    /// measured it; the fast-aligned seam does not, since alignment and the
    /// fact's own measured action now both fall inside the same debounce
    /// window. Rather than changing what
    /// <see cref="A_partition_entering_the_throttled_state_is_logged_once"/>
    /// and <see cref="Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record"/>
    /// assert, this variant restores the precondition those assertions were
    /// always written against: it forces a transition exactly like
    /// <see cref="BeginCountingAsync"/>, then waits out both the limiter's
    /// window and the debounce's TTL (the same duration, so one wait clears
    /// both) before returning — a passive wait, not a request, since the
    /// limiter's heartbeat replenishes on its own schedule regardless of
    /// traffic. No permit is spent by this method, so it returns
    /// <see cref="PermitLimit"/> in full, unlike <see cref="BeginCountingAsync"/>'s
    /// <c>PermitLimit - 1</c>.
    /// </remarks>
    private async Task<int> BeginCountingWithClearDebounceAsync()
    {
        bool refused = false;

        for (int attempt = 0; attempt < PermitLimit + 1; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(NoTokenBody(NewPath()));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused = true;
                break;
            }
        }

        refused.ShouldBeTrue(
            $"the window never refused after {PermitLimit + 1} requests — is another limiter "
            + "configuration live?");

        // Comfortably longer than both Window and the transition-log
        // debounce's TTL (the same duration) so heartbeat lateness cannot
        // leave either one still live.
        await Task.Delay(Window + TimeSpan.FromSeconds(2));

        return PermitLimit;
    }

    /// <summary>
    /// US1 acceptance scenario 2 / FR-001. The cheapest admitted shape — no
    /// token — mirrors <c>WhepAuthIntegrationTests.Authorize_without_a_token_returns_401</c>,
    /// so every one of the <c>budget + 1</c> requests answers <c>401</c>
    /// on its own merits until the ceiling engages.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>GatewayRateLimitIntegrationTests</c>' shape (send until the
    /// target status, assert it was reached) rather than inventing one.
    /// </remarks>
    [Fact]
    public async Task Authorize_refuses_a_source_over_its_window_with_429()
    {
        int budget = await BeginCountingAsync();

        HttpStatusCode last = await ExhaustWindowAsync(budget);

        last.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// FR-002: the refusal happens before the endpoint handler runs — no
    /// token parse, no signature verification, no repository read. Asserted
    /// by the <b>absence</b> of the handler's own log record for the throttled
    /// attempt, read through the fixture's log tail — never by mocking
    /// anything, per tasks.md T005.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The marker is <c>AuthorizeWhepCommandHandler</c>'s own
    /// <c>RefusedAbsentWhepAction</c> log line (<c>Log.cs:65-66</c>), which
    /// fires — with the request's path — the moment the handler's action
    /// check runs, before any token is even read. A request that omits the
    /// <c>action</c> field entirely (and the token, the cheapest admitted
    /// shape) reaches exactly this line whenever the handler is entered at
    /// all, which makes its absence the earliest possible proof that
    /// middleware short-circuited before the handler.
    /// </para>
    /// <para>
    /// One admitted request just before the throttled one carries a "sentinel"
    /// path — its own response is asserted <c>403</c> (spec 251 FR-004) before
    /// any log is read. Spec 251 FR-002 moves both log polls (the sentinel's
    /// presence, the throttled path's absence) to after every counted request
    /// has been sent, so a slow log read can never land between two requests
    /// whose timing the limiter's boundary is sensitive to.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_throttled_authorize_never_reaches_the_handler()
    {
        int budget = await BeginCountingAsync();

        for (int i = 0; i < budget - 1; i++)
        {
            HttpResponseMessage response = await PostAsync(NoActionBody(NewPath()));
            response.StatusCode.ShouldNotBe(
                HttpStatusCode.TooManyRequests,
                $"filler request {i + 1} of {budget - 1} should still be admitted; if this fails the "
                + "fact's own admitted-request budget is wrong, not the throttled assertion below.");
        }

        string sentinelPath = NewPath();
        HttpResponseMessage sentinelResponse = await PostAsync(NoActionBody(sentinelPath));

        // S7 (spec 208 review), extended by spec 251 FR-004: the sentinel's
        // own status is asserted here — before any log is read — so a
        // limiter that silently let it through (or a handler that answered
        // something other than the anonymous 403) is caught by its own
        // precondition rather than surfacing as a confusing log-absence
        // failure below.
        sentinelResponse.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"the ({budget})th request should still be admitted and refused only by the handler's own "
            + $"action check (403), not by the limiter; got {(int)sentinelResponse.StatusCode} "
            + $"{sentinelResponse.StatusCode}.");

        string throttledPath = NewPath();
        HttpResponseMessage throttledResponse = await PostAsync(NoActionBody(throttledPath));

        // S7 (spec 208 review): FR-002 is specifically about what happens to
        // a request the limiter actually refused — this was previously only
        // read inside the failure message below, so a limiter that silently
        // let this request through would still pass the log-absence check
        // (nothing to log on an admitted NoActionBody request until its own
        // 200/401/403 path runs) without this fact ever noticing its own
        // precondition had failed to hold.
        throttledResponse.StatusCode.ShouldBe(
            HttpStatusCode.TooManyRequests,
            $"request {budget + 1} in the window should have been refused by the limiter; it "
            + "was not, so the absence of a handler log below proves nothing about FR-002.");

        bool sentinelLogged = await MarkerEverAppearsInLogsAsync(sentinelPath, LogAbsenceGraceWindow);
        sentinelLogged.ShouldBeTrue(
            $"the sentinel request's own path ('{sentinelPath}') never appeared in the log tail via "
            + "RefusedAbsentWhepAction; if this fails the test's own admitted-request budget is "
            + "wrong, not the throttled assertion above. stream-distribution log:"
            + $"{Environment.NewLine}{aspire.RecentLogs(StreamDistributionResource, lines: 400)}");

        bool throttledMarkerLogged = await MarkerEverAppearsInLogsAsync(throttledPath, LogAbsenceGraceWindow);
        throttledMarkerLogged.ShouldBeFalse(
            $"path '{throttledPath}' was logged by AuthorizeWhepCommandHandler even though request "
            + $"{budget + 1} in the window should have been refused by the limiter before the "
            + $"handler ran. Response was {(int)throttledResponse.StatusCode} {throttledResponse.StatusCode}.");
    }

    /// <summary>
    /// FR-007 (US1 acceptance scenario 8): the authorize mapping's generated
    /// OpenAPI document names <c>429</c> alongside its <c>200</c>/<c>401</c>/<c>403</c>.
    /// </summary>
    [Fact]
    public async Task Authorize_declares_the_429_it_can_answer()
    {
        HttpResponseMessage document = await aspire.StreamDistribution.GetAsync("/openapi/v1.json");
        document.StatusCode.ShouldBe(
            HttpStatusCode.OK, await document.Content.ReadAsStringAsync());

        using JsonDocument openApi = await JsonDocument.ParseAsync(await document.Content.ReadAsStreamAsync());

        JsonElement responses = openApi.RootElement
            .GetProperty("paths")
            .GetProperty("/streams/authorize")
            .GetProperty("post")
            .GetProperty("responses");

        responses.TryGetProperty("429", out _).ShouldBeTrue(
            $"the authorize operation's declared responses were: "
            + string.Join(", ", responses.EnumerateObject().Select(property => property.Name)));
    }

    /// <summary>
    /// FR-005 (US1 acceptance scenario 7): a source that has exhausted the
    /// authorize ceiling is unaffected everywhere else — the health and
    /// readiness endpoints from <c>MapDefaultEndpoints</c>, and the other
    /// three <c>/streams</c> routes — because the policy is attached to the
    /// one mapping, never to the group or the pipeline at large.
    /// </summary>
    [Fact]
    public async Task Health_and_readiness_are_never_throttled()
    {
        int budget = await BeginCountingAsync();

        // S7 (spec 208 review): this fact's whole premise is that the source
        // below is actually throttled on /streams/authorize by this point —
        // discarding ExhaustWindowAsync's return value meant that if the
        // limiter stopped working entirely, every route below would trivially
        // read as "not throttled" while proving nothing about FR-005.
        (await ExhaustWindowAsync(budget)).ShouldBe(
            HttpStatusCode.TooManyRequests,
            "the window-exhaustion helper's own last response was not 429 — this fact's premise "
            + "(a source over the ceiling) does not hold, so the assertions below prove nothing.");

        HttpResponseMessage health = await aspire.StreamDistribution.GetAsync("/health");
        health.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        HttpResponseMessage alive = await aspire.StreamDistribution.GetAsync("/alive");
        alive.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        // The other three /streams routes all require sse.streams.read and no
        // token is sent, so today's answer on each is 401 — the point here is
        // only that it is not 429, i.e. that the "whep-authorize" policy was
        // never attached to the MapGroup.
        HttpResponseMessage getOne = await aspire.StreamDistribution.GetAsync(
            $"/streams/{Guid.CreateVersion7()}");
        getOne.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        HttpResponseMessage list = await aspire.StreamDistribution.GetAsync("/streams/");
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        HttpResponseMessage kioskLatency = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/kiosk-latency", new { });
        kioskLatency.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// US2 acceptance scenario 1 / FR-009: the first refusal after a
    /// partition crosses the ceiling emits exactly one structured
    /// <c>Warning</c> naming the partition and the configured limit —
    /// mirroring the <c>Interlocked</c>-guarded once-per-transition
    /// discipline <c>WhepAuthValidator.cs:142-151</c> already applies on this
    /// same path for the realm-unreachable case (ADR-0118: one sink, kept
    /// readable at exactly the moment a flood would otherwise drown it).
    /// </summary>
    [Fact]
    public async Task A_partition_entering_the_throttled_state_is_logged_once()
    {
        int budget = await BeginCountingWithClearDebounceAsync();

        // S6 (spec 208 review): anchors on a sentinel's position in the log
        // tail instead of a baseline-count delta. aspire.RecentLogs(...,
        // lines: 400) is the *entire* retained ring-buffer tail (AspireFixture
        // caps it at exactly 400 lines) — a baseline captured now and
        // subtracted from a count read later can undercount or go negative if
        // enough other log lines (Wolverine's own periodic chatter, other
        // requests) scroll the baseline's own matching lines out of the tail
        // before the later read. A sentinel line's position does not have
        // that problem: once found, everything counted after it is provably
        // after it, however much has scrolled off the far end.
        string sentinelPath = NewPath();
        HttpResponseMessage sentinelResponse = await PostAsync(NoActionBody(sentinelPath));

        // Spec 251 FR-004: the sentinel's own status is asserted before any
        // log is read — see the identical reasoning in
        // A_throttled_authorize_never_reaches_the_handler.
        sentinelResponse.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"the sentinel request should be admitted and refused only by the handler's own action "
            + $"check (403), not by the limiter; got {(int)sentinelResponse.StatusCode} "
            + $"{sentinelResponse.StatusCode}.");

        // Spec 251 FR-002: every counted request — the sentinel above, then
        // the exhaust below — is sent before any log is polled.
        HttpStatusCode last = await ExhaustWindowAsync(budget - 1);
        last.ShouldBe(
            HttpStatusCode.TooManyRequests,
            "the window-exhaustion helper's own last response was not 429 after the sentinel — this "
            + "fact's premise (a source over the ceiling) does not hold, so the count below proves "
            + "nothing.");

        bool sentinelLogged = await MarkerEverAppearsInLogsAsync(sentinelPath, LogAbsenceGraceWindow);
        sentinelLogged.ShouldBeTrue(
            $"the sentinel request's own path ('{sentinelPath}') never appeared in the log tail; "
            + "without it there is no reliable position to count throttle-transition records after. "
            + $"stream-distribution log:{Environment.NewLine}"
            + $"{aspire.RecentLogs(StreamDistributionResource, lines: 400)}");

        int transitionLogCount = await ThrottleTransitionCountSinceAsync(sentinelPath, LogAbsenceGraceWindow);

        transitionLogCount.ShouldBe(
            1,
            $"expected exactly one throttle-transition log record after '{sentinelPath}'; "
            + $"found {transitionLogCount}. stream-distribution log:{Environment.NewLine}"
            + $"{aspire.RecentLogs(StreamDistributionResource, lines: 400)}");
    }

    /// <summary>
    /// US2 acceptance scenario 2 / FR-009: once a partition is already
    /// throttled, further refused requests inside the same window do not add
    /// a second record — only the transition itself logs, exactly the
    /// discipline <c>WhepAuthValidator.cs:142-151</c>'s
    /// <c>Interlocked.Exchange</c> guard already applies to the
    /// realm-unreachable case on this same path. A flood that logged once
    /// per refusal would be the same defect spec 119 already fixed here for
    /// a different failure mode (ADR-0118: one sink, not flooded).
    /// </summary>
    /// <remarks>
    /// Drives a source past the ceiling, sends several more refused requests
    /// inside the same window (spec 251 FR-004: each individually asserted
    /// <c>429</c> — closing the gap where a repeat that happened to cross a
    /// window boundary and was silently admitted would make the count below
    /// prove nothing), then — only once every counted request has been sent
    /// (FR-002) — polls for the transition record and confirms the count
    /// still holds at one.
    /// </remarks>
    [Fact]
    public async Task Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record()
    {
        int budget = await BeginCountingWithClearDebounceAsync();

        // S6 (spec 208 review): sentinel-position anchor — see the identical
        // comment in A_partition_entering_the_throttled_state_is_logged_once.
        // A fresh sentinel also naturally excludes a prior fact's own
        // leftover transition record.
        string sentinelPath = NewPath();
        HttpResponseMessage sentinelResponse = await PostAsync(NoActionBody(sentinelPath));
        sentinelResponse.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"the sentinel request should be admitted and refused only by the handler's own action "
            + $"check (403), not by the limiter; got {(int)sentinelResponse.StatusCode} "
            + $"{sentinelResponse.StatusCode}.");

        HttpStatusCode last = await ExhaustWindowAsync(budget - 1);
        last.ShouldBe(
            HttpStatusCode.TooManyRequests,
            "the window-exhaustion helper's own last response was not 429 after the sentinel — this "
            + "fact's premise (a source over the ceiling) does not hold, so the repeats below prove "
            + "nothing.");

        for (int i = 0; i < 5; i++)
        {
            HttpResponseMessage repeat = await PostAsync(NoTokenBody(NewPath()));
            repeat.StatusCode.ShouldBe(
                HttpStatusCode.TooManyRequests,
                $"repeat refusal {i + 1} of 5 should still be refused by the limiter inside the same "
                + $"window; got {(int)repeat.StatusCode} {repeat.StatusCode}. A repeat that crossed a "
                + "window boundary and was admitted would make the counts below prove nothing about "
                + "FR-009's repeated-refusal silence.");
        }

        bool sentinelLogged = await MarkerEverAppearsInLogsAsync(sentinelPath, LogAbsenceGraceWindow);
        sentinelLogged.ShouldBeTrue(
            $"the sentinel request's own path ('{sentinelPath}') never appeared in the log tail; "
            + "without it there is no reliable position to count throttle-transition records after. "
            + $"stream-distribution log:{Environment.NewLine}"
            + $"{aspire.RecentLogs(StreamDistributionResource, lines: 400)}");

        int afterTransition = await ThrottleTransitionCountSinceAsync(sentinelPath, LogAbsenceGraceWindow);

        // Nothing to poll *for* here: on a healthy implementation the repeats
        // must not log at all, so there is no delivery to wait on. A short
        // fixed pause only guards against a slow-but-real second delivery
        // being missed by reading the tail before it lands.
        await Task.Delay(PollInterval);
        int afterRepeats = ThrottleTransitionCountSince(sentinelPath);

        afterTransition.ShouldBe(
            1,
            "the transition record itself (scenario 1) must already exist and be exactly one before "
            + "this fact can say anything about repeats; if this is 0 the flood below proves nothing.");
        afterRepeats.ShouldBe(
            afterTransition,
            $"five further refused requests inside the same window added "
            + $"{afterRepeats - afterTransition} more throttle-transition log record(s) after "
            + $"'{sentinelPath}'; FR-009 requires silence on the repeats. stream-distribution "
            + $"log:{Environment.NewLine}{aspire.RecentLogs(StreamDistributionResource, lines: 400)}");
    }

    /// <summary>
    /// Spec 251 (#2537) §7 — the adversarial-phase regression guard for the
    /// boundary race spec 251 §1.2 diagnoses: a fact that begins its counted
    /// sequence one to two heartbeats before a window boundary must still
    /// see exactly one whole window's worth of budget, because it aligns to
    /// the boundary first via <see cref="BeginCountingAsync"/> rather than
    /// assuming it is already past one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pre-positions deterministically — exhausts the window, polls every
    /// <see cref="AlignmentPollInterval"/> until admitted again (a boundary
    /// has just passed), then waits until <c>Window - 150ms</c> has elapsed
    /// since — so the fact's own body starts one to two heartbeats
    /// <b>before</b> the next boundary. This is a deliberate "drive" into the
    /// hazardous phase (ADR-0150 §1's driving use, not a synchronisation
    /// wait), not something later assertions depend on being exact: with the
    /// 4b seam, any starting phase must pass.
    /// </para>
    /// <para>
    /// Then runs <see cref="A_throttled_authorize_never_reaches_the_handler"/>'s
    /// own request shape — <c>budget - 1</c> fillers, a sentinel asserted
    /// <c>403</c>, one request asserted <c>429</c> — through the seam.
    /// Status assertions only, no log reads, so the red below is fast and
    /// unambiguous.
    /// </para>
    /// <para>
    /// <b>Phase 4a (this phase): expected red.</b> <see cref="BeginCountingAsync"/>
    /// is still the no-op, so nothing here aligns to the boundary the
    /// pre-position manufactured — the fact fails by straddle, either a
    /// filler or the sentinel refused early (a late heartbeat consumed the
    /// window first) or the final request admitted instead of refused (the
    /// boundary landed mid-sequence and reset the counter).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_fact_that_starts_just_before_a_window_boundary_still_counts_one_whole_window()
    {
        for (int i = 0; i < PermitLimit + 1; i++)
        {
            await PostAsync(NoTokenBody(NewPath()));
        }

        await PollUntilAdmittedAsync();
        await Task.Delay(Window - TimeSpan.FromMilliseconds(150));

        int budget = await BeginCountingAsync();

        for (int i = 0; i < budget - 1; i++)
        {
            HttpResponseMessage response = await PostAsync(NoActionBody(NewPath()));
            response.StatusCode.ShouldNotBe(
                HttpStatusCode.TooManyRequests,
                $"filler request {i + 1} of {budget - 1} should still be admitted; a fact that starts "
                + "just before a window boundary must still see one whole window's budget once it has "
                + "aligned to the following boundary.");
        }

        string sentinelPath = NewPath();
        HttpResponseMessage sentinelResponse = await PostAsync(NoActionBody(sentinelPath));
        sentinelResponse.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"the sentinel request should be admitted and refused only by the handler's own action "
            + $"check (403), not by the limiter; got {(int)sentinelResponse.StatusCode} "
            + $"{sentinelResponse.StatusCode}.");

        string throttledPath = NewPath();
        HttpResponseMessage throttledResponse = await PostAsync(NoActionBody(throttledPath));
        throttledResponse.StatusCode.ShouldBe(
            HttpStatusCode.TooManyRequests,
            $"request {budget + 1} in the window should have been refused by the limiter; a fact "
            + "that starts just before a boundary must still land its throttled request outside the "
            + "window it aligned to.");
    }

    /// <summary>Fresh per call so log-tail assertions can key on one request.</summary>
    private static string NewPath() => $"cam-{Guid.CreateVersion7()}";

    /// <summary>
    /// The cheapest admitted shape (T003's wording): no token, a present but
    /// harmless action. Fails at <c>AuthorizeWhepCommandHandler.HandleAsync</c>'s
    /// token-presence check without logging anything, so it is safe to reuse
    /// purely to consume the window.
    /// </summary>
    private static object NoTokenBody(string path) => new { token = (string?)null, path, action = "read" };

    /// <summary>
    /// Omits the <c>action</c> field entirely (and the token). The handler's
    /// action check runs first, so this reaches
    /// <c>Log.RefusedAbsentWhepAction</c> — the earliest log statement on this
    /// path — whenever it is reached at all, which is exactly what
    /// <see cref="A_throttled_authorize_never_reaches_the_handler"/> needs.
    /// </summary>
    private static object NoActionBody(string path) => new { token = (string?)null, path };

    /// <summary>
    /// Deliberately through <see cref="AspireFixture.StreamDistributionThrottleProbe"/>,
    /// not <see cref="AspireFixture.StreamDistribution"/>. Every call this
    /// helper makes is a candidate for a <c>429</c> once the window is
    /// exhausted, and <c>StreamDistribution</c> shares its resilience
    /// pipeline (and circuit breaker) with every other context's client this
    /// fixture hands out — see that property's doc comment (#2284 phase-5
    /// verification.md §2.2).
    /// </summary>
    private Task<HttpResponseMessage> PostAsync(object body) =>
        aspire.StreamDistributionThrottleProbe.PostAsJsonAsync("/streams/authorize", body);

    /// <summary>
    /// Sends <paramref name="budget"/> no-token requests — each asserted not
    /// <c>429</c> (spec 251 FR-004: the arithmetic this fact's count rests on
    /// is checked, not merely assumed) — then one more, and returns that
    /// last response's status.
    /// </summary>
    private async Task<HttpStatusCode> ExhaustWindowAsync(int budget)
    {
        for (int attempt = 0; attempt < budget; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(NoTokenBody(NewPath()));
            response.StatusCode.ShouldNotBe(
                HttpStatusCode.TooManyRequests,
                $"request {attempt + 1} of {budget} should still be admitted; if this fails the "
                + "fact's own admitted-request budget is wrong, not the refusal asserted after it.");
        }

        using HttpResponseMessage overflow = await PostAsync(NoTokenBody(NewPath()));
        return overflow.StatusCode;
    }

    /// <summary>
    /// Waits for the condition that a request is admitted again — never a
    /// fixed sleep or a fixed request count — so a test that exhausts the
    /// window does not poison whichever test in <see cref="AspireCollection"/>
    /// runs next (constitution §Testing).
    /// </summary>
    private async Task WaitUntilAdmittedAgainAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + AdmissionRecoveryTimeout;
        HttpStatusCode status;

        do
        {
            using HttpResponseMessage response = await PostAsync(NoTokenBody(NewPath()));
            status = response.StatusCode;

            if (status != HttpStatusCode.TooManyRequests)
            {
                // Spec 251 (#2537) FR-003: this recovery step only has to
                // leave the window in a state some later fact can safely
                // align from — it no longer guesses at the boundary itself.
                // It used to hold here for one additional full Window (spec
                // 208 review S9/S10, a fixed Task.Delay(Window) anchored at
                // this observed admission), on the theory that a delay of
                // exactly Window reliably lands the next fact just past the
                // following boundary. It does not: FixedWindowRateLimiter
                // replenishes on a heartbeat (100 ms, first tick at or after
                // Window) anchored at limiter creation, not per caller or
                // partition, so that fixed delay could drift past the
                // boundary under heartbeat lateness — handing the next fact
                // a window that already held this probe's spent permit (spec
                // 251 §1.2). Every limiter-counting fact now observes its own
                // starting boundary directly through BeginCountingAsync
                // instead of inheriting one from here.
                return;
            }

            await Task.Delay(PollInterval);
        }
        while (DateTimeOffset.UtcNow < deadline);

        status.ShouldNotBe(
            HttpStatusCode.TooManyRequests,
            $"the window did not reopen within {AdmissionRecoveryTimeout} of being exhausted; "
            + "every later test on this shared fixture's address is now at risk of a false 429.");
    }

    /// <summary>
    /// The condition-only half of a recovery poll: waits until a request is
    /// admitted again, at <see cref="AlignmentPollInterval"/>'s finer cadence
    /// and with no trailing delay. Spec 251: used today only by
    /// <see cref="A_fact_that_starts_just_before_a_window_boundary_still_counts_one_whole_window"/>'s
    /// deliberate pre-position; from phase 4b, <see cref="BeginCountingAsync"/>'s
    /// own boundary detection shares this same condition.
    /// </summary>
    private async Task PollUntilAdmittedAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + AdmissionRecoveryTimeout;
        HttpStatusCode status;

        do
        {
            using HttpResponseMessage response = await PostAsync(NoTokenBody(NewPath()));
            status = response.StatusCode;

            if (status != HttpStatusCode.TooManyRequests)
            {
                return;
            }

            await Task.Delay(AlignmentPollInterval);
        }
        while (DateTimeOffset.UtcNow < deadline);

        status.ShouldNotBe(
            HttpStatusCode.TooManyRequests,
            $"the window did not reopen within {AdmissionRecoveryTimeout} of being exhausted; "
            + "every later test on this shared fixture's address is now at risk of a false 429.");
    }

    /// <summary>
    /// Polls the fixture's log tail for <paramref name="marker"/> and returns
    /// whether it ever appeared within <paramref name="timeout"/>. Mirrors
    /// <c>LogTailDeliversIntegrationTests.PollForAsync</c>'s shape, but answers
    /// a boolean because both callers here need to tell "arrived" from
    /// "definitely never arriving" rather than surface the last tail read.
    /// </summary>
    private async Task<bool> MarkerEverAppearsInLogsAsync(string marker, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (aspire.RecentLogs(StreamDistributionResource).Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(PollInterval);
        }

        return aspire.RecentLogs(StreamDistributionResource).Contains(marker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Polls up to <paramref name="settleTimeout"/> for
    /// <see cref="ThrottleTransitionCountSince"/> to become non-zero, then
    /// returns whatever the count is at that point (which is <c>0</c> if it
    /// never did). Mirrors <see cref="MarkerEverAppearsInLogsAsync"/>'s
    /// settle-then-read shape, but returns a count rather than a boolean
    /// because <see cref="Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record"/>
    /// needs to see the count hold, not merely appear.
    /// </summary>
    /// <param name="settleTimeout">How long to keep polling for the count to move off zero.</param>
    /// <param name="sentinelPath">
    /// The caller's own fact-local marker — see <see cref="ThrottleTransitionCountSince"/>.
    /// </param>
    private async Task<int> ThrottleTransitionCountSinceAsync(string sentinelPath, TimeSpan settleTimeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + settleTimeout;
        int count = ThrottleTransitionCountSince(sentinelPath);

        while (count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval);
            count = ThrottleTransitionCountSince(sentinelPath);
        }

        return count;
    }

    /// <summary>
    /// S6 (spec 208 review): counts throttle-transition markers (see
    /// <see cref="ThrottleTransitionMarkers"/>) that appear <b>after</b>
    /// <paramref name="sentinelPath"/>'s own line in the log tail, rather
    /// than a raw-count-minus-baseline delta. <c>aspire.RecentLogs(...,
    /// lines: 400)</c> is the entire retained ring-buffer tail; enough
    /// intervening log lines (Wolverine's own chatter, other requests) can
    /// scroll a baseline's own matching lines out of the tail before a later
    /// read, making a subtracted delta undercount or go negative. A
    /// sentinel's position does not have that problem: everything counted
    /// after it is provably after it, however much has scrolled off the far
    /// end of the tail.
    /// </summary>
    /// <param name="sentinelPath">
    /// A path unique to this fact, already confirmed present in the tail (via
    /// <see cref="MarkerEverAppearsInLogsAsync"/>) before this is called —
    /// callers post a <see cref="NoActionBody"/> request with this path and
    /// wait for it to appear, establishing a position every later line in
    /// this fact's own behaviour is guaranteed to follow.
    /// </param>
    private int ThrottleTransitionCountSince(string sentinelPath)
    {
        string[] lines = aspire.RecentLogs(StreamDistributionResource, lines: 400)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        int sentinelIndex = Array.FindLastIndex(
            lines, line => line.Contains(sentinelPath, StringComparison.Ordinal));

        // If the sentinel itself has already scrolled out of the tail, there
        // is no reliable "since" boundary left — read as "none yet" rather
        // than silently counting from the start of the tail, so a caller
        // polling via ThrottleTransitionCountSinceAsync keeps trying instead
        // of reporting a false count.
        if (sentinelIndex < 0)
        {
            return 0;
        }

        return lines
            .Skip(sentinelIndex + 1)
            .Count(line => ThrottleTransitionMarkers.Any(
                marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }
}
