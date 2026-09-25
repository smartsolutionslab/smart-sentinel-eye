using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// ADR-0113 Layer 1 for Identity (spec 012 T040), against the real stack — so
/// the versions here are ones the EF interceptor actually moved, which no
/// Application-layer fake reproduces. Since spec 254 (#2570) the webhook
/// rotation gate also covers Layer 2: a genuine two-request race that both
/// pass the Layer-1 version check before either commits, decided only by the
/// EF concurrency token on <c>SaveAsync</c>.
///
/// <para>
/// The gate applies to the webhook rotation only. The device and kiosk
/// disables were reviewed out: a disable is terminal and
/// <c>GetWithinFabAsync</c> stops returning the row, so their version cannot
/// move while they are still reachable, and requiring a precondition there
/// would have been a breaking change buying nothing.
/// </para>
///
/// <para>
/// Rotation is the genuine lost update: two admins rotating concurrently
/// leaves the first holding a secret the second invalidated. The conflict
/// test proves the refused rotation left the live credential working by
/// authenticating with it, not by comparing it to another freshly minted
/// secret — that comparison passes even when the credential has been
/// destroyed.
/// </para>
///
/// <para>
/// No per-test reset: rotating creates real Keycloak clients, and wiping the
/// Postgres rows would leave those behind and desynchronised. Each test mints
/// its own name, as <c>NFR002_MqttConnectAuthTests</c> already does.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RegisteredClientConcurrencyIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string Fab = "munich";

    /// <summary>How many rotations race per round in <see cref="A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down"/>.</summary>
    private const int Racers = 6;

    /// <summary>Rounds attempted before giving up as inconclusive (spec 254 FR-006).</summary>
    private const int MaxRounds = 10;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Disabling_a_device_needs_no_precondition()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = await RegisterDeviceAsync(identity);

        HttpResponseMessage disabled = await identity.DeleteAsync($"/devices/{clientId}?fabId={Fab}");

        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(disabled));
        (await FindDeviceAsync(identity, clientId)).GetProperty("disabledAt").ValueKind
            .ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_rotation_without_a_precondition_is_refused_with_428()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");

        HttpResponseMessage refused = await identity.PostAsync(
            $"/webhook-integrations/{UniqueIntegrationName()}/rotate", Body());

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task A_created_client_is_listed_with_the_version_its_next_rotation_needs()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        int returned = await CreateAsync(identity, name);

        // The read path exists so a caller who lost the rotation response is
        // not locked out of ever rotating again.
        (await FindWebhookAsync(identity, name)).GetProperty("version").GetInt32().ShouldBe(returned);
    }

    [Fact]
    public async Task Each_rotation_returns_the_version_the_next_one_must_send()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();

        // Chained three deep on purpose. Two would still pass if the response
        // version were frozen at 0, because an Added root is not bumped — the
        // third is what proves the value tracks the interceptor.
        int afterCreate = await CreateAsync(identity, name);
        int afterSecond = await RotateAsync(identity, name, afterCreate);
        int afterThird = await RotateAsync(identity, name, afterSecond);

        afterSecond.ShouldBeGreaterThan(afterCreate);
        afterThird.ShouldBeGreaterThan(afterSecond);
    }

    [Fact]
    public async Task A_rotation_superseded_by_another_admin_leaves_the_live_secret_working()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        int shared = await CreateAsync(identity, name);

        // Both admins hold `shared`. The first wins and its secret is live.
        (int version, string secret) winner = await RotateForSecretAsync(identity, name, shared);

        HttpResponseMessage refused = await identity.SendAsync(
            Conditional(name, shared, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_STALE");

        // The assertion that matters: the winner's credential still
        // authenticates. Comparing it to another freshly rotated secret would
        // pass even if the refused request had destroyed it.
        (await CanAuthenticateAsync($"webhook-{name}", winner.secret))
            .ShouldBeTrue("the refused rotation invalidated the live secret");
    }

    /// <summary>
    /// Spec 254 AS-1 (#2570) — the confirmation. <see cref="Racers"/> rotations
    /// at the same If-Match version, dispatched together, so several are past
    /// the Layer-1 read-then-compare before any has committed — the window
    /// only the EF concurrency token on <c>SaveAsync</c> (Layer 2) decides.
    /// Repeats rounds against the winner's new version until a Layer-2 loser
    /// is actually observed, because a single round usually only exercises
    /// Layer 1 (sequential staleness against an already-committed version).
    ///
    /// <para>
    /// <b>Pre-fix red, and what it means</b> (spec 254 §5): a round containing
    /// a 502 <c>KEYCLOAK_UNAVAILABLE</c> loser is the confirmed defect — the
    /// Layer-2 loser's raw <see cref="DbUpdateConcurrencyException"/> was
    /// caught by <c>RotateWebhookClientCommandHandler</c>'s filter and
    /// misreported as a Keycloak outage. A 500 loser means the exception
    /// arrived <i>wrapped</i> after all, contradicting spec 254 §1/A1 — that is
    /// a stop-and-report case, the fix would belong in middleware, not this
    /// handler. An <c>AGGREGATE_VERSION_STALE</c> loser never appearing across
    /// all <see cref="MaxRounds"/> rounds is inconclusive (A2): the harness
    /// never actually got two requests racing at Layer 2.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        int version = await CreateAsync(identity, name);

        bool layer2Observed = false;
        for (int round = 1; round <= MaxRounds && !layer2Observed; round++)
        {
            HttpResponseMessage[] answers = await Task.WhenAll(Enumerable.Range(0, Racers)
                .Select(_ => identity.SendAsync(Conditional(name, version, Body()))));

            (HttpStatusCode Status, string? Title, string Raw)[] results =
                await Task.WhenAll(answers.Select(ReadAnswerAsync));

            // A loser's body is quoted whole: a 502's detail carries the caught
            // exception's message, which is what tells a Layer-2 loser from a real
            // Keycloak outage. A 200's body holds the new secret, so it is not.
            string answered = string.Join(", ", results.Select(r => r.Status == HttpStatusCode.OK
                ? "200"
                : $"{(int)r.Status} {r.Title ?? "(no title)"} {r.Raw}"));

            (HttpStatusCode Status, string? Title, string Raw)[] winners = results
                .Where(r => r.Status == HttpStatusCode.OK).ToArray();
            winners.Length.ShouldBe(1,
                $"round {round}: exactly one racer may win the rotation, its version and secret become "
                + $"the round's outcome. Answers: {answered}");

            (HttpStatusCode Status, string? Title, string Raw)[] losers = results
                .Where(r => r.Status != HttpStatusCode.OK).ToArray();

            bool any502 = losers.Any(loser => loser.Status == HttpStatusCode.BadGateway);
            bool any500 = losers.Any(loser => loser.Status == HttpStatusCode.InternalServerError);
            string meaning;
            if (any502)
            {
                meaning = "a 502 KEYCLOAK_UNAVAILABLE loser is spec 254's confirmed defect: the Layer-2 "
                    + "loser's raw DbUpdateConcurrencyException was caught by the handler's filter and "
                    + "misreported as a Keycloak outage.";
            }
            else if (any500)
            {
                meaning = "a 500 loser means the exception arrived wrapped after all (contradicts spec "
                    + "254 §1/A1) — STOP and report verbatim, the fix would belong in middleware, not "
                    + "the handler filter.";
            }
            else
            {
                meaning = "an unexpected non-409 answer — report verbatim rather than treating it as "
                    + "either known case.";
            }
            losers.ShouldAllBe(
                loser => loser.Status == HttpStatusCode.Conflict
                    && (loser.Title == "WEBHOOK_CLIENT_STALE" || loser.Title == "AGGREGATE_VERSION_STALE"),
                customMessage: $"round {round}: every loser must be told it conflicted — 409 "
                    + $"WEBHOOK_CLIENT_STALE or 409 AGGREGATE_VERSION_STALE, never anything else. {meaning} "
                    + $"Answers: {answered}");

            using JsonDocument winnerBody = JsonDocument.Parse(winners[0].Raw);
            int winnerVersion = winnerBody.RootElement.GetProperty("version").GetInt32();
            string winnerSecret = winnerBody.RootElement.GetProperty("clientSecret").GetString()!;

            (await CanAuthenticateAsync($"webhook-{name}", winnerSecret)).ShouldBeTrue(
                $"round {round}: the round's winner must still be able to authenticate with the secret "
                + $"it was just handed. Answers: {answered}");

            if (losers.Any(loser => loser.Title == "AGGREGATE_VERSION_STALE"))
            {
                layer2Observed = true;
                break;
            }

            version = winnerVersion;
        }

        layer2Observed.ShouldBeTrue(
            $"inconclusive: no racer reached Layer 2 in {MaxRounds} rounds of {Racers} — the harness did "
            + "not race deep enough to put two requests between the Layer-1 version check and the "
            + "commit. This is not spec 254's defect's red (spec 254 §5 A2); report it rather than "
            + "declaring victory.");
    }

    private static async Task<(HttpStatusCode Status, string? Title, string Raw)> ReadAnswerAsync(
        HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync();
        string? title = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("title", out JsonElement titleElement))
            {
                title = titleElement.GetString();
            }
        }
        catch (JsonException)
        {
            // A body that is not JSON at all (an unmapped 500, say) — the raw
            // text still carries the evidence into the assertion message.
        }

        return (response.StatusCode, title, raw);
    }

    [Fact]
    public async Task Rotating_a_client_that_does_not_exist_creates_nothing()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();

        HttpResponseMessage refused = await identity.SendAsync(Conditional(name, 0, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_NOT_FOUND");
        (await ListWebhooksAsync(identity)).EnumerateArray()
            .ShouldNotContain(row => row.GetProperty("clientId").GetString() == $"webhook-{name}");
    }

    [Fact]
    public async Task Re_creating_an_existing_client_does_not_roll_its_secret()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        await CreateAsync(identity, name);
        (int version, string secret) live = await RotateForSecretAsync(identity, name, 0);

        // The replayed first-time rotation: If-None-Match: * against a client
        // that now exists.
        HttpResponseMessage refused = await identity.SendAsync(CreateConditional(name, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_ALREADY_EXISTS");
        (await CanAuthenticateAsync($"webhook-{name}", live.secret))
            .ShouldBeTrue("the refused create rolled the existing secret");
    }

    /// <summary>
    /// Client-credentials grant with the rotated secret. This is what the
    /// webhook sender does, so it is the only assertion that actually
    /// distinguishes a live credential from a dead one.
    /// </summary>
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
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);

        return token.IsSuccessStatusCode;
    }

    private async Task<string> RegisterDeviceAsync(HttpClient identity)
    {
        HttpResponseMessage created = await identity.PostAsJsonAsync(
            $"/devices/register?fabId={Fab}",
            new { deviceType = "plc", deviceIdentifier = $"t040-{Guid.CreateVersion7():N}" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));

        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("clientId").GetString()!;
    }

    private async Task<int> CreateAsync(HttpClient identity, string name)
    {
        HttpResponseMessage created = await identity.SendAsync(CreateConditional(name, Body()));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));

        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();
    }

    private async Task<int> RotateAsync(HttpClient identity, string name, int expectedVersion) =>
        (await RotateForSecretAsync(identity, name, expectedVersion)).Version;

    private async Task<(int Version, string Secret)> RotateForSecretAsync(
        HttpClient identity, string name, int expectedVersion)
    {
        HttpResponseMessage rotated = await identity.SendAsync(
            Conditional(name, expectedVersion, Body()));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));

        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("version").GetInt32(), body.GetProperty("clientSecret").GetString()!);
    }

    private static async Task<JsonElement> ListWebhooksAsync(HttpClient identity)
    {
        HttpResponseMessage listed = await identity.GetAsync($"/webhook-integrations?fabId={Fab}");
        listed.EnsureSuccessStatusCode();

        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> FindWebhookAsync(HttpClient identity, string name) =>
        (await ListWebhooksAsync(identity)).EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("clientId").GetString(), $"webhook-{name}", StringComparison.Ordinal));

    /// <summary>
    /// The list is fab-wide and shared with every other test in the run, so the
    /// row is found by clientId rather than by position.
    /// </summary>
    private static async Task<JsonElement> FindDeviceAsync(HttpClient identity, string clientId)
    {
        HttpResponseMessage listed = await identity.GetAsync($"/devices?fabId={Fab}");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("clientId").GetString(), clientId, StringComparison.Ordinal));
    }

    private static JsonContent Body() => JsonContent.Create(new { fabId = Fab });

    private static HttpRequestMessage Conditional(string name, int version, JsonContent content)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    private static HttpRequestMessage CreateConditional(string name, JsonContent content)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        return request;
    }

    private static async Task<string> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    // Keycloak's client-id grammar allows letters, digits, '.', '_' and '-',
    // and the handler prefixes this with "webhook-".
    private static string UniqueIntegrationName() => $"t040-{Guid.CreateVersion7():N}";

    private Task<string> DiagnoseAsync(HttpResponseMessage response) =>
        aspire.DiagnoseAsync("identity", response);
}
