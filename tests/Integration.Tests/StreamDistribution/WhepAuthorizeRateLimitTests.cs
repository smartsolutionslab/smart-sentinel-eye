using System.Text.Json;
using System.Text.RegularExpressions;
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
/// poison whichever test runs next. <b>Corrected (spec 208 review S10):</b> once
/// admission is observed, <see cref="WaitUntilAdmittedAgainAsync"/> deliberately
/// holds for one additional full <c>Window</c> before returning — a fixed
/// <c>Task.Delay(Window)</c>, but layered on top of the condition wait, not
/// instead of it. See that method's own comment (S9) for why a delay of
/// exactly <c>Window</c>, anchored at the moment admission was confirmed, is
/// load-bearing rather than an arbitrary safety margin.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WhepAuthorizeRateLimitTests(AspireFixture aspire)
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
    /// US1 acceptance scenario 2 / FR-001. The cheapest admitted shape — no
    /// token — mirrors <c>WhepAuthIntegrationTests.Authorize_without_a_token_returns_401</c>,
    /// so every one of the <c>PermitLimit + 1</c> requests answers <c>401</c>
    /// on its own merits until the ceiling engages.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>GatewayRateLimitIntegrationTests</c>' shape (send until the
    /// target status, assert it was reached) rather than inventing one.
    /// <b>Expected red:</b> nothing throttles today, so every response —
    /// including the last — is <c>401</c>, and the assertion fails comparing
    /// <c>Unauthorized</c> to <c>TooManyRequests</c>.
    /// </remarks>
    [Fact]
    public async Task Authorize_refuses_a_source_over_its_window_with_429()
    {
        HttpStatusCode last = await ExhaustWindowAsync();

        last.ShouldBe(HttpStatusCode.TooManyRequests);

        await WaitUntilAdmittedAgainAsync();
    }

    /// <summary>
    /// US1 acceptance scenario 3 / FR-003. The scenario that distinguishes a
    /// per-source partition from the rejected global-bucket design — a design
    /// under which one anonymous caller could exhaust the whole window and
    /// deny MediaMTX itself (plan.md §Partition key).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Substitution, not the original design — stated explicitly per
    /// tasks.md T004's own fallback clause.</b> The original version of this
    /// test (committed at <c>ba9c5080</c>) posted from a second, explicitly
    /// <c>127.0.0.2</c>-bound outbound socket to observe two genuinely
    /// distinguishable remote addresses land in separate partitions. A
    /// temporary diagnostic probe (since removed) confirmed that does not
    /// hold in this topology: <c>stream-distribution</c>'s HTTP endpoint runs
    /// behind Aspire's DCP proxy in dev/test (<c>WithHttpEndpoint()</c>,
    /// default-proxied, <c>AppHost.cs</c>), and the proxy terminates every
    /// inbound connection and re-originates it from its own loopback socket —
    /// so <b>both</b> the fixture's default client and a
    /// <c>127.0.0.2</c>-bound client arrive at Kestrel as <c>remote=::1</c>.
    /// The two "sources" the original test constructed are not actually
    /// distinguishable at the layer the rate limiter partitions on, once an
    /// intervening proxy sits in front — the original doc comment assumed
    /// direct connection, and that assumption is false here.
    /// </para>
    /// <para>
    /// <b>tasks.md T004 pre-authorizes exactly this fallback</b>: "If varying
    /// the source address from the test host proves impossible, assert the
    /// partition-key shape instead and say so explicitly — do not silently
    /// drop the scenario." This is that fallback, not a weakened assertion —
    /// it reads <c>Program.cs</c>'s limiter registration and asserts the
    /// <i>design property</i> scenario 3 exists to rule out: the partition
    /// key is built from the connection's remote address, not a fixed or
    /// global bucket, so two different remote addresses necessarily land in
    /// two different partitions. That is a fact about the registration, not
    /// an observation that requires two addresses to actually reach Kestrel
    /// distinguishably — which, through this proxy, they cannot.
    /// </para>
    /// <para>
    /// <b>Still not phase-4a red evidence</b>, for the same reason as before:
    /// nothing about this shape depends on the limiter being wired up, so it
    /// would pass equally before or after T007. It remains a standing design
    /// guard against a global-bucket regression, not red evidence — tasks.md
    /// T004 says so explicitly, and the PR must not present it as satisfying
    /// the phase-4a gate.
    /// </para>
    /// </remarks>
    [Fact]
    public void Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket()
    {
        string programSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepositoryRoot().FullName, "src", "StreamDistribution", "Api", "Program.cs"));

        // S4 (spec 208 review): the raw source between the registration call
        // and the options factory carries Program.cs's own explanatory
        // comment lines (e.g. "// Source IP, not a global bucket ...
        // RemoteIpAddress ..."), so searching the raw text would keep passing
        // if the real key argument were reverted to a fixed literal while a
        // comment mentioning RemoteIpAddress stayed behind. Strip comment
        // lines first, mirroring ResilienceRegistrationTests.CodeLines/IsComment.
        string codeOnly = string.Join(Environment.NewLine, CodeLines(programSource));

        int policyRegistration = codeOnly.IndexOf(
            "AddPolicy(\"whep-authorize\"", StringComparison.Ordinal);
        policyRegistration.ShouldBeGreaterThanOrEqualTo(
            0,
            "Program.cs no longer registers a \"whep-authorize\" policy by that name — the "
            + "partition-key shape below cannot be checked against a registration that is not there.");

        int limiterCall = codeOnly.IndexOf(
            "RateLimitPartition.GetFixedWindowLimiter(", policyRegistration, StringComparison.Ordinal);
        limiterCall.ShouldBeGreaterThan(
            policyRegistration,
            "expected the \"whep-authorize\" policy to build its partition via "
            + "RateLimitPartition.GetFixedWindowLimiter.");

        int keyArgumentStart = limiterCall + "RateLimitPartition.GetFixedWindowLimiter(".Length;
        int keyArgumentEnd = codeOnly.IndexOf(
            "_ => new FixedWindowRateLimiterOptions", keyArgumentStart, StringComparison.Ordinal);
        keyArgumentEnd.ShouldBeGreaterThan(
            keyArgumentStart,
            "could not find the fixed-window options factory that follows the partition-key "
            + "argument — the registration's shape has moved.");

        string partitionKeyArgument = codeOnly[keyArgumentStart..keyArgumentEnd].Trim().TrimEnd(',').Trim();

        // FR-003: the key must vary with the caller's remote address, never a
        // fixed literal — a fixed key is exactly the rejected global-bucket
        // design that would let one anonymous caller exhaust MediaMTX's own
        // window (plan.md §Partition key, spec §"One global bucket").
        partitionKeyArgument.ShouldContain(
            "context.Connection.RemoteIpAddress",
            Case.Sensitive,
            "the \"whep-authorize\" policy's partition-key argument was (comments stripped):"
            + $"{Environment.NewLine}{partitionKeyArgument}"
            + $"{Environment.NewLine}FR-003 requires it to be built from the connection's remote "
            + "address, not a fixed/global bucket.");

        // S4's explicit negative check: the positive assertion above would
        // already fail if the key reverted to a fixed literal that also
        // dropped the "RemoteIpAddress" substring — this makes that failure
        // mode a named check rather than an incidental one. A bare quoted
        // literal with no interpolation hole is, by shape alone, the
        // rejected global-bucket design, regardless of what it happens to be
        // named.
        BareLiteralShape.IsMatch(partitionKeyArgument).ShouldBeFalse(
            "the partition-key argument reads as a single fixed string literal with no interpolation "
            + $"hole: '{partitionKeyArgument}'. FR-003 requires the key to vary with the caller's "
            + "connection — a bare literal is exactly the rejected global-bucket design that would "
            + "let one anonymous caller exhaust MediaMTX's own window.");
    }

    /// <summary>
    /// Matches a complete double-quoted string literal — interpolated or
    /// not — with no <c>{</c> anywhere inside it, spanning the whole
    /// (trimmed) key argument. The current key,
    /// <c>$"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"</c>,
    /// contains a <c>{</c> before its closing quote and never matches; a
    /// regression to a fixed key such as <c>"whep-authorize-bucket"</c> would.
    /// </summary>
    private static readonly Regex BareLiteralShape = new(
        @"^\$?""[^{]*""$", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Mirrors <c>ResilienceRegistrationTests.IsComment</c>.</summary>
    private static bool IsComment(string line) =>
        line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    /// <summary>Mirrors <c>ResilienceRegistrationTests.CodeLines</c>.</summary>
    private static IEnumerable<string> CodeLines(string source) =>
        source.Split('\n').Where(line => !IsComment(line));

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
    /// path, polled for in the tail first — establishing that log delivery for
    /// this exact request shape is not merely slow before trusting an absence
    /// for the very next one.
    /// </para>
    /// <para>
    /// <b>Expected red:</b> today every request reaches the handler, so the
    /// throttled request's own marker appears in the tail exactly like the
    /// sentinel's did, and the assertion that it must be absent fails.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_throttled_authorize_never_reaches_the_handler()
    {
        for (int i = 0; i < PermitLimit - 1; i++)
        {
            await PostAsync(NoActionBody(NewPath()));
        }

        string sentinelPath = NewPath();
        await PostAsync(NoActionBody(sentinelPath));

        bool sentinelLogged = await MarkerEverAppearsInLogsAsync(sentinelPath, LogAbsenceGraceWindow);
        sentinelLogged.ShouldBeTrue(
            $"the ({PermitLimit})th request should still be admitted and should have logged its own "
            + "path via RefusedAbsentWhepAction; if this fails the test's own admitted-request budget "
            + "is wrong, not the throttled assertion below.");

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
            $"request {PermitLimit + 1} in the window should have been refused by the limiter; it "
            + $"was not, so the absence of a handler log below proves nothing about FR-002.");

        bool throttledMarkerLogged = await MarkerEverAppearsInLogsAsync(throttledPath, LogAbsenceGraceWindow);
        throttledMarkerLogged.ShouldBeFalse(
            $"path '{throttledPath}' was logged by AuthorizeWhepCommandHandler even though request "
            + $"{PermitLimit + 1} in the window should have been refused by the limiter before the "
            + $"handler ran. Response was {(int)throttledResponse.StatusCode} {throttledResponse.StatusCode}.");

        await WaitUntilAdmittedAgainAsync();
    }

    /// <summary>
    /// FR-007 (US1 acceptance scenario 8): the authorize mapping's generated
    /// OpenAPI document names <c>429</c> alongside its <c>200</c>/<c>401</c>/<c>403</c>.
    /// </summary>
    /// <remarks>
    /// <b>Expected red:</b> <c>StreamEndpoints.cs:63-80</c> declares only
    /// <c>200</c>, <c>401</c> and <c>403</c> today (no <c>.ProducesProblem(StatusCodes.Status429TooManyRequests)</c>
    /// yet), so the generated document carries no <c>429</c> response for this
    /// operation.
    /// </remarks>
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
    /// <remarks>
    /// Not red today for a different reason than the other four facts: with
    /// no limiter installed at all, every route is trivially "never throttled"
    /// regardless of how many authorize requests precede it. It starts proving
    /// something only once T007-T008 exist — a standing guard, not phase-4a
    /// red evidence, the same distinction
    /// <see cref="Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket"/>
    /// makes for a different reason (that one never depends on T007-T008 at
    /// all, since it reads <c>Program.cs</c>'s source rather than observing
    /// runtime behaviour).
    /// </remarks>
    [Fact]
    public async Task Health_and_readiness_are_never_throttled()
    {
        // S7 (spec 208 review): this fact's whole premise is that the source
        // below is actually throttled on /streams/authorize by this point —
        // discarding ExhaustWindowAsync's return value meant that if the
        // limiter stopped working entirely, every route below would trivially
        // read as "not throttled" while proving nothing about FR-005.
        (await ExhaustWindowAsync()).ShouldBe(
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

        await WaitUntilAdmittedAgainAsync();
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
    /// <remarks>
    /// <b>Expected red:</b> the count is <c>0</c>, not <c>1</c> — no
    /// <c>OnRejected</c> callback is registered today, so nothing logs
    /// anything about the rate limiter's own state transitions.
    /// </remarks>
    [Fact]
    public async Task A_partition_entering_the_throttled_state_is_logged_once()
    {
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
        await PostAsync(NoActionBody(sentinelPath));
        bool sentinelLogged = await MarkerEverAppearsInLogsAsync(sentinelPath, LogAbsenceGraceWindow);
        sentinelLogged.ShouldBeTrue(
            $"the sentinel request's own path ('{sentinelPath}') never appeared in the log tail; "
            + "without it there is no reliable position to count throttle-transition records after.");

        await ExhaustWindowAsync();

        int transitionLogCount = await ThrottleTransitionCountSinceAsync(sentinelPath, LogAbsenceGraceWindow);

        transitionLogCount.ShouldBe(
            1,
            $"expected exactly one throttle-transition log record after '{sentinelPath}'; "
            + $"found {transitionLogCount}. stream-distribution log:{Environment.NewLine}"
            + $"{aspire.RecentLogs(StreamDistributionResource)}");

        await WaitUntilAdmittedAgainAsync();
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
    /// Drives a source past the ceiling, waits for the transition record,
    /// then sends several more refused requests inside the same window and
    /// asserts the count has not grown beyond the one transition.
    /// <b>Expected red:</b> the count never reaches <c>1</c> at all — today
    /// nothing logs anything about the rate limiter's own state, so the
    /// transition this asserts happened first is itself unmet (count stays
    /// at <c>0</c> where FR-009 requires it to reach and hold at <c>1</c>).
    /// </remarks>
    [Fact]
    public async Task Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record()
    {
        // S6 (spec 208 review): sentinel-position anchor — see the identical
        // comment in A_partition_entering_the_throttled_state_is_logged_once.
        // A fresh sentinel also naturally excludes a prior fact's own
        // leftover transition record (e.g. left by
        // A_throttled_authorize_never_reaches_the_handler throwing before it
        // reached WaitUntilAdmittedAgainAsync): that record sits before this
        // fact's own sentinel, so it is never counted.
        string sentinelPath = NewPath();
        await PostAsync(NoActionBody(sentinelPath));
        bool sentinelLogged = await MarkerEverAppearsInLogsAsync(sentinelPath, LogAbsenceGraceWindow);
        sentinelLogged.ShouldBeTrue(
            $"the sentinel request's own path ('{sentinelPath}') never appeared in the log tail; "
            + "without it there is no reliable position to count throttle-transition records after.");

        await ExhaustWindowAsync();

        int afterTransition = await ThrottleTransitionCountSinceAsync(sentinelPath, LogAbsenceGraceWindow);

        for (int i = 0; i < 5; i++)
        {
            await PostAsync(NoTokenBody(NewPath()));
        }

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
            + $"log:{Environment.NewLine}{aspire.RecentLogs(StreamDistributionResource)}");

        await WaitUntilAdmittedAgainAsync();
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
    /// Sends <c>PermitLimit + 1</c> no-token requests and returns the last
    /// response's status — the shape tasks.md T003 asks for.
    /// </summary>
    private async Task<HttpStatusCode> ExhaustWindowAsync()
    {
        HttpStatusCode last = HttpStatusCode.OK;
        for (int attempt = 0; attempt < PermitLimit + 1; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(NoTokenBody(NewPath()));
            last = response.StatusCode;
        }

        return last;
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
                // The admission probe above consumed one permit slot in
                // *this* window. Returning immediately would hand the next
                // fact in AspireCollection's sequence (milliseconds later,
                // well inside this 10s test-mode Window) a window with 1
                // slot already spent rather than a fresh PermitLimit-sized
                // budget — exactly what poisoned
                // A_throttled_authorize_never_reaches_the_handler's own
                // admitted-request math.
                //
                // S9 (spec 208 review) — the stronger, load-bearing reason a
                // full extra Window is needed, not merely "one slot back":
                // FixedWindowRateLimiter replenishes on a timer anchored at
                // limiter *creation*, not per caller and not per partition.
                // A fact that starts at an arbitrary phase within that
                // server-wide window boundary only has whatever time remains
                // until the *next* boundary to run its own admit/exhaust
                // sequence — which can be anywhere from almost a full Window
                // down to almost none. Delaying a full Window after an
                // *observed* admission reliably lands the next fact just
                // past the following boundary instead, handing it close to a
                // full Window of its own to work with regardless of where in
                // the server's cycle this fact happened to run. Do not
                // "optimise" this down to Window/2 or similar — that
                // reintroduces exactly the intermittent, phase-dependent
                // flakiness this delay exists to remove, with nothing left
                // to explain it.
                await Task.Delay(Window);
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

    /// <summary>
    /// The directory holding <c>SmartSentinelEye.slnx</c>, walking up from
    /// <see cref="AppContext.BaseDirectory"/>. Same shape as the copies in
    /// <c>AppHostE2ESwitchTests</c> and <c>AppHostStackStatusTests</c> — this
    /// file is source-scanning <c>Program.cs</c> for
    /// <see cref="Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket"/>,
    /// not asserting HTTP behaviour, so it needs its own path to the repo
    /// root rather than the fixture's base address.
    /// </summary>
    private static System.IO.DirectoryInfo RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
