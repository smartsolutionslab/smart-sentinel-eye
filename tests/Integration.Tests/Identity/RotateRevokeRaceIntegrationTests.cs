using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 318 (#2628), the adversarial half: what the revocation check on
/// <c>POST /webhook-integrations/{name}/rotate</c> must still hold when the
/// revoke is not neatly finished before the rotation starts.
/// <c>RotateRevokedWebhookIntegrationIntegrationTests</c> covers the
/// sequential defect; this class covers what a check can get wrong around it.
///
/// <para>
/// Four properties, each against the real stack, because each depends on two
/// contexts' databases, the outbox and Keycloak interleaving for real:
/// </para>
/// <list type="number">
/// <item><b>The check is a reader, not a writer.</b> A refused rotation must
/// leave EventIngestion's integration exactly as the revoke left it. A rotation
/// that is allowed through publishes <c>WebhookIntegrationRotatedV1</c>, whose
/// subscriber calls <c>MarkAsRotated</c> on the revoked row and moves its
/// version, so the revoke's own optimistic-concurrency chain is disturbed by a
/// request that should have done nothing.</item>
/// <item><b>The check reads committed state on every request.</b> An
/// integration rotated twice (an <c>Active</c> answer seen twice) and then
/// revoked must be refused by the very next rotation. An implementation that
/// caches the status per name passes every sequential fact that uses fresh
/// names, and fails this one.</item>
/// <item><b>Linearisability.</b> A rotation sent after the revoke's 200 was
/// received can only be refused, whichever branch it would take.</item>
/// <item><b>Convergence.</b> Whatever the interleaving, once both requests
/// have answered and the asynchronous disable (spec 264) has had 20 s, a
/// revoked integration has no enabled Keycloak client and no secret handed out
/// for it mints a token.</item>
/// </list>
///
/// <para>
/// <b>Property 4 is stricter than spec 318 §3's accepted residual</b>, on
/// purpose. On the create branch, a revoke whose announcement Identity
/// processes while the rotation is between its status read and its row's
/// <c>SaveAsync</c> finds no row, settles as <c>WebhookClientNotFound</c>, and
/// the client the rotation then registers stays enabled. That span includes the
/// whole of <c>CreateClientAsync</c>, not only the gap after it. If a round
/// fails here after the fix, that window is what it observed: report it, do not
/// widen the assertion.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RotateRevokeRaceIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string Fab = "munich";
    private const string RevokedCode = "WEBHOOK_INTEGRATION_REVOKED";

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Delay between dispatching the revoke and dispatching the rotation, one
    /// round each. The zeros put both in flight together; the larger values
    /// let the revoke commit and its announcement travel before the rotation
    /// reads, which is the ordering a stale or early read gets wrong.
    /// </summary>
    private static readonly int[] StaggersMilliseconds = [0, 0, 0, 5, 15, 30, 60, 120, 250, 500];

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("event-ingestion", KnownResourceStates.Running, cts.Token);

        // Warm both services with a GET before the first single-attempt POST
        // (ADR-0143), as RegisteredClientConcurrencyIntegrationTests does: a race
        // whose first request also pays listener, JWKS and EF model start-up
        // measures start-up, not the interleaving.
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity", cts.Token);
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion", cts.Token);
        (await identity.GetAsync($"/webhook-integrations?fabId={Fab}", cts.Token)).EnsureSuccessStatusCode();
        (await events.GetAsync("/webhook-integrations?includeRevoked=true", cts.Token)).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Property 1. The control integration is rotated <b>after</b> the revoked
    /// one, on the same announcement path, and its EventIngestion version is
    /// observed moving: so by the time the revoked row is read, an announcement
    /// for it would have landed too, and "unchanged" is evidence, not a timeout.
    /// </summary>
    [Fact]
    public async Task A_refused_rotation_leaves_the_revoked_integration_in_event_ingestion_untouched()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string revoked = UniqueName("rr-quiet");
        string control = UniqueName("rr-ctl");

        await RegisterAsync(events, revoked);
        await RegisterAsync(events, control);
        (await RevokeAsync(events, revoked, await VersionAsync(events, revoked)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        int revokedVersion = await VersionAsync(events, revoked);
        int controlVersion = await VersionAsync(events, control);

        Answer refusedOrNot = await ReadAnswerAsync(await identity.SendAsync(CreateIntent(revoked)));
        Answer controlRotation = await ReadAnswerAsync(await identity.SendAsync(CreateIntent(control)));
        controlRotation.Status.ShouldBe(
            HttpStatusCode.OK,
            $"the control integration is active and must rotate — this is the control, not the claim. {controlRotation}");

        int controlNow = await PollVersionUntilAsync(events, control, version => version != controlVersion);
        controlNow.ShouldNotBe(
            controlVersion,
            $"the control's WebhookIntegrationRotatedV1 must reach EventIngestion within {Settle.TotalSeconds} s; "
            + "without that, 'the revoked row did not move' would prove nothing");

        await Task.Delay(TimeSpan.FromSeconds(2));
        int revokedNow = await VersionAsync(events, revoked);

        revokedNow.ShouldBe(
            revokedVersion,
            $"the rotation of revoked '{revoked}' answered {refusedOrNot}, and EventIngestion's revoked row moved "
            + $"from version {revokedVersion} to {revokedNow}: a rotation that should have been refused published "
            + "WebhookIntegrationRotatedV1, and EventIngestion's subscriber called MarkAsRotated on a revoked "
            + "integration. The status check let a writer through");
        refusedOrNot.Status.ShouldBe(HttpStatusCode.Conflict, refusedOrNot.ToString());
        refusedOrNot.Title.ShouldBe(RevokedCode, refusedOrNot.ToString());
    }

    /// <summary>
    /// Property 2. Two successful rotations put an <c>Active</c> answer in front
    /// of the check twice; then the revoke; then, immediately, the rotation that
    /// must be refused. Immediately matters: spec 264's disable is asynchronous,
    /// so the local row may still be enabled and the rotate branch still
    /// reachable — the third mode spec 318 §1 names, here end to end.
    /// </summary>
    [Fact]
    public async Task A_rotation_after_the_revoke_answered_is_refused_even_when_the_same_integration_just_rotated()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueName("rr-warm");

        await RegisterAsync(events, name);
        Answer created = await ReadAnswerAsync(await identity.SendAsync(CreateIntent(name)));
        created.Status.ShouldBe(HttpStatusCode.OK, $"control: the active integration must create. {created}");
        Answer rolled = await ReadAnswerAsync(await identity.SendAsync(RotateIntent(name, created.Version())));
        rolled.Status.ShouldBe(HttpStatusCode.OK, $"control: the active integration must rotate. {rolled}");

        await RevokeSettledAsync(events, name);

        Answer after = await ReadAnswerAsync(await identity.SendAsync(RotateIntent(name, rolled.Version())));

        after.Status.ShouldBe(
            HttpStatusCode.Conflict,
            $"a rotation sent after EventIngestion answered 200 to the revoke must be refused, even though the "
            + $"two rotations before it saw '{name}' active. Answered: {after}");
        after.Title.ShouldBe(RevokedCode, after.ToString());
        after.Raw.ShouldNotContain("clientSecret", customMessage: "a refused rotation must not hand out a secret");

        (await EnabledClientCountAfterSettlingAsync($"webhook-{name}")).ShouldBe(
            0, $"'{name}' is revoked; its client must be disabled within {Settle.TotalSeconds} s");
        (await CanAuthenticateAsync($"webhook-{name}", rolled.Secret())).ShouldBeFalse(
            "the last secret issued before the revoke must stop minting tokens");
    }

    /// <summary>
    /// Properties 3 and 4 on the create branch: an integration that has never
    /// been rotated, a revoke and a first rotation in flight together.
    /// Every round runs to completion before anything is asserted, so a red
    /// shows the whole distribution rather than the first bad round.
    /// </summary>
    [Fact]
    public async Task A_revoke_racing_a_first_rotation_never_leaves_a_revoked_integration_with_an_enabled_client()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");

        List<string> rounds = [];
        List<string> violations = [];
        int overlapped = 0;

        foreach (int stagger in StaggersMilliseconds)
        {
            string name = UniqueName("rr-new");
            await RegisterAsync(events, name);
            int version = await VersionAsync(events, name);

            Race race = await RaceAsync(
                () => RevokeAsync(events, name, version),
                () => identity.SendAsync(CreateIntent(name)),
                stagger);

            await JudgeAsync(events, name, race, stagger, secretsIssued: [], rounds, violations);
            overlapped += race.Overlapped ? 1 : 0;
        }

        string report = string.Join(Environment.NewLine, rounds);
        violations.ShouldBeEmpty(
            $"create branch, {StaggersMilliseconds.Length} rounds:{Environment.NewLine}{report}");
        overlapped.ShouldBeGreaterThan(
            0, $"inconclusive: no round had the rotation in flight before the revoke answered.{Environment.NewLine}{report}");
    }

    /// <summary>
    /// Properties 3 and 4 on the rotate branch: the integration already has an
    /// enabled client at version V, and a revoke races a secret roll at V.
    /// Here the revoke announcement always finds the row (it was committed
    /// before the race), so convergence is the claim spec 318 §3 makes for
    /// this branch, and both the pre-race secret and any rolled one must die.
    /// </summary>
    [Fact]
    public async Task A_revoke_racing_a_secret_roll_never_leaves_a_usable_secret_behind()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");

        List<string> rounds = [];
        List<string> violations = [];
        int overlapped = 0;

        foreach (int stagger in StaggersMilliseconds)
        {
            string name = UniqueName("rr-roll");
            await RegisterAsync(events, name);
            int registered = await VersionAsync(events, name);

            Answer created = await ReadAnswerAsync(await identity.SendAsync(CreateIntent(name)));
            created.Status.ShouldBe(HttpStatusCode.OK, $"control: the active integration must create. {created}");

            // The first rotation's announcement moves EventIngestion's version
            // (MarkAsRotated). Wait for it, or the racing revoke's If-Match is
            // stale for a reason that has nothing to do with the race.
            int version = await PollVersionUntilAsync(events, name, v => v != registered);
            version.ShouldNotBe(registered, "control: WebhookIntegrationRotatedV1 must reach EventIngestion");

            Race race = await RaceAsync(
                () => RevokeAsync(events, name, version),
                () => identity.SendAsync(RotateIntent(name, created.Version())),
                stagger);

            List<string> secrets = [created.Secret()];
            if (race.Rotate.Status == HttpStatusCode.OK)
            {
                secrets.Add(race.Rotate.Secret());
            }

            await JudgeAsync(events, name, race, stagger, secrets, rounds, violations);
            overlapped += race.Overlapped ? 1 : 0;
        }

        string report = string.Join(Environment.NewLine, rounds);
        violations.ShouldBeEmpty(
            $"rotate branch, {StaggersMilliseconds.Length} rounds:{Environment.NewLine}{report}");
        overlapped.ShouldBeGreaterThan(
            0, $"inconclusive: no round had the rotation in flight before the revoke answered.{Environment.NewLine}{report}");
    }

    private async Task JudgeAsync(
        HttpClient events,
        string name,
        Race race,
        int stagger,
        IReadOnlyList<string> secretsIssued,
        List<string> rounds,
        List<string> violations)
    {
        bool isRevoked = await IsRevokedAsync(events, name);
        int enabled = isRevoked ? await EnabledClientCountAfterSettlingAsync($"webhook-{name}") : -1;

        List<string> broken = [];
        if (race.RevokeAnsweredFirst && race.Rotate.Status != HttpStatusCode.Conflict)
        {
            broken.Add($"rotation sent {race.GapMilliseconds} ms after the revoke's 200 was not refused");
        }

        if (race.Rotate.Status == HttpStatusCode.Conflict && race.Rotate.Title != RevokedCode
            && race.Revoke.Status == HttpStatusCode.OK)
        {
            broken.Add($"a refusal of a revoked integration carried {race.Rotate.Title}, not {RevokedCode}");
        }

        if (enabled > 0)
        {
            broken.Add($"EventIngestion says revoked, Keycloak still holds {enabled} enabled client(s) after {Settle.TotalSeconds} s");
        }

        if (isRevoked)
        {
            foreach (string secret in secretsIssued)
            {
                if (await CanAuthenticateAsync($"webhook-{name}", secret))
                {
                    broken.Add("a secret issued for the integration still mints a token after the revoke settled");
                }
            }
        }

        string line = $"stagger {stagger,3} ms | revoke {(int)race.Revoke.Status} | rotate {(int)race.Rotate.Status} "
            + $"{race.Rotate.Title ?? string.Empty} | {(race.RevokeAnsweredFirst ? $"revoke answered {race.GapMilliseconds} ms before rotate was sent" : "overlapped")} "
            + $"| revoked={isRevoked} enabledClients={(enabled < 0 ? "n/a" : enabled.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
            + (broken.Count == 0 ? string.Empty : $" | VIOLATION: {string.Join("; ", broken)}");
        rounds.Add(line);
        if (broken.Count > 0)
        {
            violations.Add(line);
        }
    }

    /// <summary>
    /// Dispatches the revoke, waits <paramref name="staggerMilliseconds"/>, then
    /// dispatches the rotation. Timestamps are taken on one monotonic clock in
    /// this process, so "the revoke's answer was received before the rotation
    /// was sent" is a real happens-before on the server, not an estimate.
    /// </summary>
    private static async Task<Race> RaceAsync(
        Func<Task<HttpResponseMessage>> revoke,
        Func<Task<HttpResponseMessage>> rotate,
        int staggerMilliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        long revokeAnsweredAt = long.MaxValue;

        Task<Answer> revoking = Task.Run(async () =>
        {
            HttpResponseMessage response = await revoke();
            Interlocked.Exchange(ref revokeAnsweredAt, clock.ElapsedTicks);
            return await ReadAnswerAsync(response);
        });

        if (staggerMilliseconds > 0)
        {
            await Task.Delay(staggerMilliseconds);
        }

        long rotateSentAt = clock.ElapsedTicks;
        Task<Answer> rotating = Task.Run(async () => await ReadAnswerAsync(await rotate()));

        Answer[] answers = await Task.WhenAll(revoking, rotating);
        long answered = Interlocked.Read(ref revokeAnsweredAt);

        bool revokeFirst = answers[0].Status == HttpStatusCode.OK && answered < rotateSentAt;
        long gap = revokeFirst ? (rotateSentAt - answered) * 1000 / Stopwatch.Frequency : 0;

        return new Race(answers[0], answers[1], revokeFirst, !revokeFirst && answers[0].Status == HttpStatusCode.OK, gap);
    }

    /// <summary>
    /// Polls Keycloak for up to <see cref="Settle"/> until no enabled client
    /// with this id remains, and answers how many enabled ones were left. Zero
    /// clients counts as converged: the create branch refused before
    /// Keycloak was called.
    /// </summary>
    private async Task<int> EnabledClientCountAfterSettlingAsync(string clientId)
    {
        DateTime deadline = DateTime.UtcNow + Settle;
        int enabled = await EnabledClientCountAsync(clientId);
        while (enabled > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(Poll);
            enabled = await EnabledClientCountAsync(clientId);
        }

        return enabled;
    }

    private async Task<int> EnabledClientCountAsync(string clientId)
    {
        RealmProbe realm = new(aspire);
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);

        return clients.EnumerateArray().Count(client => client.GetProperty("enabled").GetBoolean());
    }

    private async Task<bool> CanAuthenticateAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage token = await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token", form);

        return token.IsSuccessStatusCode;
    }

    private static async Task RegisterAsync(HttpClient events, string name)
    {
        HttpResponseMessage registered = await events.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        registered.StatusCode.ShouldBe(
            HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient events, string name, int version)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return events.SendAsync(request);
    }

    /// <summary>
    /// Revokes against whatever version EventIngestion holds now, retrying a
    /// stale answer: the rotations before it publish announcements whose
    /// <c>MarkAsRotated</c> moves the version asynchronously. Landing before the
    /// revoke's read gives 412; landing between its read and its save gives
    /// 409 <c>AGGREGATE_VERSION_STALE</c>. Neither is what this test is about.
    /// </summary>
    private static async Task RevokeSettledAsync(HttpClient events, string name)
    {
        DateTime deadline = DateTime.UtcNow + Settle;
        HttpResponseMessage revoked;
        do
        {
            revoked = await RevokeAsync(events, name, await VersionAsync(events, name));
            if (revoked.StatusCode is not (HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict))
            {
                break;
            }

            await Task.Delay(Poll);
        }
        while (DateTime.UtcNow < deadline);

        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
    }

    private static async Task<int> PollVersionUntilAsync(HttpClient events, string name, Func<int, bool> moved)
    {
        DateTime deadline = DateTime.UtcNow + Settle;
        int version = await VersionAsync(events, name);
        while (!moved(version) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(Poll);
            version = await VersionAsync(events, name);
        }

        return version;
    }

    private static async Task<int> VersionAsync(HttpClient events, string name) =>
        (await FindAsync(events, name)).GetProperty("version").GetInt32();

    private static async Task<bool> IsRevokedAsync(HttpClient events, string name) =>
        (await FindAsync(events, name)).GetProperty("revokedAt").ValueKind != JsonValueKind.Null;

    private static async Task<JsonElement> FindAsync(HttpClient events, string name)
    {
        HttpResponseMessage listed = await events.GetAsync("/webhook-integrations?includeRevoked=true");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private static HttpRequestMessage CreateIntent(string name)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return request;
    }

    private static HttpRequestMessage RotateIntent(string name, int version)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return request;
    }

    private static async Task<Answer> ReadAnswerAsync(HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync();
        string? title = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("title", out JsonElement titleElement))
            {
                title = titleElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON at all — the raw text still reaches the assertion message.
        }

        return new Answer(response.StatusCode, title, raw);
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}";

    private sealed record Answer(HttpStatusCode Status, string? Title, string Raw)
    {
        public int Version()
        {
            using JsonDocument document = JsonDocument.Parse(Raw);
            return document.RootElement.GetProperty("version").GetInt32();
        }

        public string Secret()
        {
            using JsonDocument document = JsonDocument.Parse(Raw);
            return document.RootElement.GetProperty("clientSecret").GetString()!;
        }

        // A 200's body carries a live secret; it is never echoed into a message.
        public override string ToString() =>
            Status == HttpStatusCode.OK ? "200 (secret withheld)" : $"{(int)Status} {Title ?? "(no title)"} {Raw}";
    }

    private sealed record Race(
        Answer Revoke, Answer Rotate, bool RevokeAnsweredFirst, bool Overlapped, long GapMilliseconds);
}
