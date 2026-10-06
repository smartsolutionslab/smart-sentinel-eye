using System.Net.Http.Headers;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 302 (#2205) — the decision on #2205 keeps the collapsed 401
/// (revoked vs. never-existed are indistinguishable to the caller, pinned by
/// spec 102's <c>WebhookRevocationRefusesDeliveryIntegrationTests</c>) but
/// adds a purely internal audit line: when the lookup in
/// <c>EventsEndpoints.Writes.AuthenticateWebhookAsync</c> finds a
/// <b>revoked</b> integration specifically, the endpoint must write a
/// structured log record saying so. When the lookup finds nothing at all
/// (an unknown name), no such record is written — that branch is unchanged.
///
/// <para>
/// <b>Red today.</b> The endpoint currently collapses "not found" and
/// "revoked" into the same <c>null</c> at one condition, with nothing logged
/// either way. Nothing here changes the external 401 (asserted first, in
/// every fact, before any log is read — the response and the log are two
/// different concerns).
/// </para>
///
/// <para>
/// <b>Keyed on the full phrase, never the name alone.</b> The revocation-time
/// record (<c>Application/Log.cs</c>'s <c>WebhookIntegrationRevoked</c>,
/// "Revoked webhook integration '{Name}' ({Identifier}).") already names the
/// integration and fires on every revoke in this file's own setup. A
/// name-only match would make every fact here a false pass against that
/// pre-existing record, so every assertion matches the longer, distinct
/// phrase plan §4 specifies: <c>Refused a delivery to revoked webhook
/// integration '&lt;name&gt;'</c>.
/// </para>
///
/// <para>
/// Helpers below (register/capture token, revoke with <c>If-Match</c>, post a
/// delivery, find/conditional/unique-name/diagnose) are copied verbatim from
/// <c>WebhookRevocationRefusesDeliveryIntegrationTests</c> rather than
/// extracted from it — that file is spec 102's characterisation baseline and
/// must not be edited, even to share code, under this slice's phase-4a rules.
/// </para>
///
/// <para>
/// No <c>[Trait("Category", …)]</c>: mirrors every other file in this folder
/// that is not Measurement/Disruptive/Maintenance, so the untraited CI filter
/// picks this class up.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RevokedWebhookDeliveryIsLoggedIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private const string EventIngestionResource = "event-ingestion";

    private static readonly TimeSpan LogAppearanceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly JsonElement Payload =
        JsonDocument.Parse("""{"severity":"high"}""").RootElement;

    /// <summary>
    /// Acceptance scenario 1 (spec.md §3): a delivery to a revoked integration
    /// is refused exactly as today (401, empty body) and, internally, logged
    /// as a revoked-delivery refusal naming the integration.
    /// </summary>
    [Fact]
    public async Task A_delivery_to_a_revoked_integration_is_logged_as_revoked()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName("revoked-logged");
        string token = await RegisterAndCaptureTokenAsync(admin, name);
        await RevokeAsync(admin, name);

        using LogCapture capture = aspire.CaptureLogs(EventIngestionResource);

        HttpResponseMessage refused = await PostWebhookAsync(name, Fab, token);

        // The response is checked first and in full — status and body — before
        // any log is read. This is the pinned, unchanged half of the behaviour
        // (spec 102): nothing about the external contract may move.
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await DiagnoseAsync(refused));
        (await refused.Content.ReadAsStringAsync()).ShouldBeEmpty(
            "the 401 body must stay empty — this fact's new assertion is about an internal log "
            + "record only, never about what the caller receives.");

        string expectedPhrase = $"Refused a delivery to revoked webhook integration '{name}'";
        bool logged = await MarkerEverAppearsAsync(capture, expectedPhrase, LogAppearanceTimeout);

        logged.ShouldBeTrue(
            $"expected the {EventIngestionResource} log to contain a record matching "
            + $"\"{expectedPhrase}\" after a delivery was refused because the integration was "
            + $"revoked; found none. Captured lines:{Environment.NewLine}{CapturedLines(capture)}");
    }

    /// <summary>
    /// Acceptance scenario 2 (spec.md §3): a delivery to a name that was never
    /// registered writes no revocation record. The never-registered name is
    /// POSTed to first; a second, different integration is then
    /// registered-and-revoked purely as a sentinel, so that finding its own
    /// revoked-delivery record proves the capture and the polling actually
    /// work — only then does "no record names the never-registered name"
    /// mean anything, rather than trivially passing because nothing logs
    /// anything yet.
    /// </summary>
    [Fact]
    public async Task A_delivery_to_an_integration_that_never_existed_logs_no_revocation()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        string neverRegisteredName = UniqueName("never-existed");

        using LogCapture capture = aspire.CaptureLogs(EventIngestionResource);

        HttpResponseMessage neverRegisteredResponse = await PostWebhookAsync(
            neverRegisteredName, Fab, "irrelevant-token");
        neverRegisteredResponse.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized, await DiagnoseAsync(neverRegisteredResponse));

        string sentinelName = UniqueName("sentinel-revoked");
        string sentinelToken = await RegisterAndCaptureTokenAsync(admin, sentinelName);
        await RevokeAsync(admin, sentinelName);

        HttpResponseMessage sentinelResponse = await PostWebhookAsync(sentinelName, Fab, sentinelToken);
        sentinelResponse.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized, await DiagnoseAsync(sentinelResponse));

        string sentinelPhrase = $"Refused a delivery to revoked webhook integration '{sentinelName}'";
        bool sentinelLogged = await MarkerEverAppearsAsync(capture, sentinelPhrase, LogAppearanceTimeout);
        sentinelLogged.ShouldBeTrue(
            $"the sentinel's own revoked-delivery record (\"{sentinelPhrase}\") never appeared; "
            + "without it, finding no record naming the never-registered integration proves "
            + $"nothing about this fact. Captured lines:{Environment.NewLine}{CapturedLines(capture)}");

        capture.Contains(neverRegisteredName).ShouldBeFalse(
            $"a log record mentioned the never-registered integration name '{neverRegisteredName}', "
            + "but no revocation-refusal record should ever be written for a lookup that found "
            + $"nothing at all. Captured lines:{Environment.NewLine}{CapturedLines(capture)}");
    }

    /// <summary>
    /// Acceptance scenario 4 (spec.md §3): the revoked-delivery record never
    /// carries the credential. Posts the (now-revoked) bearer in the
    /// Authorization header and asserts it never appears anywhere in the
    /// captured log, once the revoked-delivery record itself is confirmed
    /// present.
    /// </summary>
    [Fact]
    public async Task The_revoked_delivery_record_never_carries_the_bearer()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName("revoked-bearer");
        string token = await RegisterAndCaptureTokenAsync(admin, name);
        await RevokeAsync(admin, name);

        using LogCapture capture = aspire.CaptureLogs(EventIngestionResource);

        HttpResponseMessage refused = await PostWebhookAsync(name, Fab, token);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await DiagnoseAsync(refused));

        string expectedPhrase = $"Refused a delivery to revoked webhook integration '{name}'";
        bool logged = await MarkerEverAppearsAsync(capture, expectedPhrase, LogAppearanceTimeout);
        logged.ShouldBeTrue(
            $"expected the {EventIngestionResource} log to contain a record matching "
            + $"\"{expectedPhrase}\" before this fact can check what it carries; found none. "
            + $"Captured lines:{Environment.NewLine}{CapturedLines(capture)}");

        capture.Contains(token).ShouldBeFalse(
            $"the captured {EventIngestionResource} log contained the revoked integration's own "
            + "bearer token value; the revoked-delivery record must never carry the credential. "
            + $"Captured lines:{Environment.NewLine}{CapturedLines(capture)}");
    }

    /// <summary>
    /// Review finding on #2205 (phase 6, both reviewers): every fact above
    /// posts with <c>fabId</c> equal to the integration's own fab, so nothing
    /// here proves the log call actually runs before
    /// <c>AuthenticateWebhookAsync</c>'s <c>IsIntegrationsOwnFab</c> check — a
    /// regression that moved it after would still pass every fact above — and
    /// the "never logs the caller-supplied fabId" property is untested, since
    /// caller fabId and the integration's own fab are identical everywhere
    /// else. Registers and revokes an integration in <see cref="Fab"/>
    /// ("munich"), then posts the delivery naming a different, syntactically
    /// valid fab ("dresden") as the caller's <c>fabId</c>. The 401 must stay
    /// unchanged; the record must still name the integration's own fab
    /// (proving the log ran even though the fab check would have refused
    /// first) and must never name the caller-supplied one.
    ///
    /// <para>
    /// Uses its own, distinct integration — never one another fact in this
    /// file already revoked — because the rate-limiting fix for #2205's
    /// review logs at most once per integration per window
    /// (<c>IsNewRevokedWebhookDeliveryLog</c>); sharing an integration with
    /// another fact's refused delivery in the same run could silently
    /// suppress this fact's own record.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_revoked_delivery_with_a_mismatched_fabId_still_logs_the_integrations_own_fab()
    {
        const string callerFab = "dresden";

        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName("revoked-mismatched-fab");
        string token = await RegisterAndCaptureTokenInFabAsync(admin, name, Fab);
        await RevokeAsync(admin, name);

        using LogCapture capture = aspire.CaptureLogs(EventIngestionResource);

        HttpResponseMessage refused = await PostWebhookAsync(name, callerFab, token);

        // (a) the external 401 is unchanged by which fab the caller named —
        // checked first and in full, exactly as the facts above do.
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await DiagnoseAsync(refused));
        (await refused.Content.ReadAsStringAsync()).ShouldBeEmpty(
            "the 401 body must stay empty regardless of which fabId the caller named.");

        string expectedPhrase = $"Refused a delivery to revoked webhook integration '{name}'";
        bool logged = await MarkerEverAppearsAsync(capture, expectedPhrase, LogAppearanceTimeout);
        logged.ShouldBeTrue(
            $"expected the {EventIngestionResource} log to contain a record matching "
            + $"\"{expectedPhrase}\" even though the caller named a fab other than the integration's "
            + $"own; found none. Captured lines:{Environment.NewLine}{CapturedLines(capture)}");

        string matchedLine = capture.Lines.First(line => line.Contains(expectedPhrase, StringComparison.Ordinal));

        // (b) the record names the integration's OWN fab — proving the log
        // call ran before (or regardless of) the fab-rejection check, which
        // would have refused on "dresden" and never reached this branch had
        // the ordering regressed.
        matchedLine.ShouldContain(
            $"in fab {Fab}",
            Case.Sensitive,
            $"the revoked-delivery record must name the integration's own fab ('{Fab}'); it did not, "
            + $"which means the log either moved after the fab check or stopped naming the "
            + $"integration's own fab. Line: {matchedLine}");

        // (c) the record never carries the caller-supplied, mismatched fabId.
        matchedLine.ShouldNotContain(
            callerFab,
            Case.Sensitive,
            $"the revoked-delivery record carried the caller-supplied fabId ('{callerFab}'), which it "
            + $"must never do — only the integration's own fab is ever logged. Line: {matchedLine}");
    }

    // ---- copied (not refactored) from WebhookRevocationRefusesDeliveryIntegrationTests ----

    /// <summary>
    /// The plaintext bearer exists exactly once, in the registration response —
    /// the row keeps only a hash — so it is captured here and held in a local.
    /// </summary>
    private static async Task<string> RegisterAndCaptureTokenAsync(HttpClient admin, string name)
    {
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("token").GetString()!;
    }

    /// <summary>
    /// Revoke carrying the version the listing just reported (ADR-0113). The
    /// 200 is asserted because a 428 or 409 here would otherwise surface as an
    /// unexplained failure further down.
    /// </summary>
    private async Task RevokeAsync(HttpClient admin, string name)
    {
        int version = (await FindAsync(admin, name)).GetProperty("version").GetInt32();

        using HttpRequestMessage request = Conditional(name, version);
        HttpResponseMessage revoked = await admin.SendAsync(request);

        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(revoked));
    }

    private static async Task<JsonElement> FindAsync(HttpClient admin, string name)
    {
        HttpResponseMessage listed = await admin.GetAsync("/webhook-integrations?includeRevoked=false");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private static HttpRequestMessage Conditional(string name, int version)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string name, string fabId, string bearer)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"/events/webhook/{name}?fabId={fabId}")
        {
            Content = JsonContent.Create(new { payload = Payload }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        return await aspire.EventIngestion.SendAsync(request);
    }

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 33)];

    private Task<string> DiagnoseAsync(HttpResponseMessage response) =>
        aspire.DiagnoseAsync("event-ingestion", response);

    // ---- new to this file ----

    /// <summary>
    /// Like <see cref="RegisterAndCaptureTokenAsync"/>, but names the fab
    /// explicitly rather than relying on the admin's default resolution — so
    /// <see cref="A_revoked_delivery_with_a_mismatched_fabId_still_logs_the_integrations_own_fab"/>
    /// can be certain which fab the integration belongs to, independent of
    /// the admin account's own fab membership.
    /// </summary>
    private static async Task<string> RegisterAndCaptureTokenInFabAsync(HttpClient admin, string name, string fab)
    {
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"/webhook-integrations?fabId={fab}", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("token").GetString()!;
    }

    /// <summary>
    /// Polls <paramref name="capture"/> for <paramref name="marker"/> at
    /// <see cref="PollInterval"/> until it appears or <paramref name="timeout"/>
    /// elapses. Mirrors <c>WhepAuthorizeRateLimitTests.MarkerEverAppearsInLogsAsync</c>'s
    /// shape — the idiom plan §5 points at for reading this fixture's log
    /// capture.
    /// </summary>
    private static async Task<bool> MarkerEverAppearsAsync(LogCapture capture, string marker, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (capture.Contains(marker))
            {
                return true;
            }

            await Task.Delay(PollInterval);
        }

        return capture.Contains(marker);
    }

    private static string CapturedLines(LogCapture capture) =>
        string.Join(Environment.NewLine, capture.Lines);
}
