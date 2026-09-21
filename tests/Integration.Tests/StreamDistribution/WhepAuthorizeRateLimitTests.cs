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
/// <b>The test-mode ceiling, not the production one.</b> <c>AppHost.cs:419-424</c>
/// overrides <c>WhepAuthorizeRateLimiting__PermitLimit=20</c> and
/// <c>:Window=00:00:10</c> for the integration lane (<c>isE2ETests</c>), because
/// the production ceiling (2000/min) cannot be exhausted from a test host without
/// poisoning every other test on this shared fixture's one address for the rest
/// of that minute (plan.md §Test-mode ceiling). <c>PermitLimit</c> is written
/// here as the literal <c>20</c> rather than read live from the running
/// service's configuration: no existing test helper resolves a downstream
/// service's bound <c>IConfiguration</c> from the AppHost's own DI container
/// (<c>aspire.App.Services</c> is the orchestrator's container, not
/// stream-distribution's), and unlike a production security parameter this is
/// the test's <i>own</i> fixture configuration — the number this file was told
/// to expect, not a value whose drift would go unnoticed.
/// </para>
///
/// <para>
/// <b>Every test that exhausts the window waits for it to reopen before
/// returning</b> (never a fixed <c>Task.Delay</c> count — constitution
/// §Testing), because <c>AspireCollection</c> serialises every test class here
/// against one running stack; a test that returns with the window still
/// exhausted would poison whichever test runs next.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WhepAuthorizeRateLimitTests(AspireFixture aspire)
{
    private const string StreamDistributionResource = "stream-distribution";

    /// <summary>
    /// Matches <c>AppHost.cs:422</c>'s <c>isE2ETests</c> override, not the
    /// production default in <c>appsettings.json</c> — see the class doc for
    /// why this is a literal rather than a live config read.
    /// </summary>
    private const int PermitLimit = 20;

    /// <summary>Matches <c>AppHost.cs:423</c>'s <c>isE2ETests</c> override.</summary>
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

        int policyRegistration = programSource.IndexOf(
            "AddPolicy(\"whep-authorize\"", StringComparison.Ordinal);
        policyRegistration.ShouldBeGreaterThanOrEqualTo(
            0,
            "Program.cs no longer registers a \"whep-authorize\" policy by that name — the "
            + "partition-key shape below cannot be checked against a registration that is not there.");

        int limiterCall = programSource.IndexOf(
            "RateLimitPartition.GetFixedWindowLimiter(", policyRegistration, StringComparison.Ordinal);
        limiterCall.ShouldBeGreaterThan(
            policyRegistration,
            "expected the \"whep-authorize\" policy to build its partition via "
            + "RateLimitPartition.GetFixedWindowLimiter.");

        int keyArgumentStart = limiterCall + "RateLimitPartition.GetFixedWindowLimiter(".Length;
        int keyArgumentEnd = programSource.IndexOf(
            "_ => new FixedWindowRateLimiterOptions", keyArgumentStart, StringComparison.Ordinal);
        keyArgumentEnd.ShouldBeGreaterThan(
            keyArgumentStart,
            "could not find the fixed-window options factory that follows the partition-key "
            + "argument — the registration's shape has moved.");

        string partitionKeyArgument = programSource[keyArgumentStart..keyArgumentEnd];

        // FR-003: the key must vary with the caller's remote address, never a
        // fixed literal — a fixed key is exactly the rejected global-bucket
        // design that would let one anonymous caller exhaust MediaMTX's own
        // window (plan.md §Partition key, spec §"One global bucket").
        partitionKeyArgument.ShouldContain(
            "context.Connection.RemoteIpAddress",
            Case.Sensitive,
            "the \"whep-authorize\" policy's partition-key argument was:"
            + $"{Environment.NewLine}{partitionKeyArgument}"
            + $"{Environment.NewLine}FR-003 requires it to be built from the connection's remote "
            + "address, not a fixed/global bucket.");
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
        await ExhaustWindowAsync();

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

    private Task<HttpResponseMessage> PostAsync(object body) =>
        aspire.StreamDistribution.PostAsJsonAsync("/streams/authorize", body);

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
