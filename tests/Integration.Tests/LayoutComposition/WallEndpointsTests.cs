using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 258 US1 (T013), against the real Aspire stack (ADR-0103). Covers
/// US1-1 through US1-15 from spec.md §4's Gherkin block: creating a wall,
/// switching it by hand (Next / a named scene), the PD-6 unpublished-scene
/// conflicts, name uniqueness, the bad-request scene-set validation table,
/// If-Match/428/409 (ADR-0113), the kiosk-scope 403 (PD-4), fab isolation
/// (spec 017 precedent), and US1-15's Reconfigured pointer move.
///
/// <para>
/// Each scenario asserts the HTTP response; US1-1 and US1-3 additionally poll
/// the audit trail for the corresponding <c>WallConfiguredV1</c> /
/// <c>WallSceneChangedV1</c> row (mirrors
/// <c>CameraAddressAuditIntegrationTests</c>). The SignalR frame itself
/// (US1-3's "kiosk receives a frame") is covered by
/// <c>e2e/wall-changes-its-scene.spec.ts</c> (T015), not here — no server
/// test can observe a browser applying a push.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WallEndpointsTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string MultiFabOperatorPassword = SeededCredentials.OpMulti;
    private const string DresdenOperatorPassword = SeededCredentials.OpDresden;

    public Task InitializeAsync() => aspire.ResetLayoutCompositionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- US1-1 / US1-2: create -----------------------------------------------

    [Fact]
    public async Task US1_1_Creating_a_wall_with_two_Published_scenes_succeeds_and_is_audited()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var name = UniqueName();

        var created = await CreateWallAsync(layouts, name, [a, b], key: $"key-{Guid.CreateVersion7():N}");
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        var wall = await created.Content.ReadFromJsonAsync<Guid>();

        var fetched = await layouts.GetAsync($"/walls/{wall}");
        fetched.EnsureSuccessStatusCode();
        var body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("scenes").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([a, b]);
        body.GetProperty("showing").GetGuid().ShouldBe(a);
        body.GetProperty("sceneVersion").GetInt64().ShouldBe(0L);
        fetched.Headers.ETag.ShouldNotBeNull();

        (Guid Wall, string Name) audited = await PollForAuditAsync<(Guid, string)>(
            "WallConfiguredV1", wall,
            "(payload->>'Wall') || '|' || (payload->>'Name')",
            row => (Guid.Parse(row.Split('|')[0]), row.Split('|')[1]));
        audited.Name.ShouldBe(name);
    }

    [Fact]
    public async Task US1_2_A_replayed_create_with_the_same_idempotency_key_returns_the_original_wall()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var name = UniqueName();
        var key = $"key-{Guid.CreateVersion7():N}";

        var first = await CreateWallAsync(layouts, name, [a, b], key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(first));
        var firstId = await first.Content.ReadFromJsonAsync<Guid>();

        var second = await CreateWallAsync(layouts, name, [a, b], key);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(second));
        var secondId = await second.Content.ReadFromJsonAsync<Guid>();

        secondId.ShouldBe(firstId, "a replay must return the wall the first attempt created, not a second one");
        (await ListNamesAsync(layouts)).Count(n => n == name).ShouldBe(1);
    }

    // ---- US1-3 / US1-4 / US1-5 / US1-6: switching -----------------------------

    [Fact]
    public async Task US1_3_A_manual_next_switch_advances_the_scene_and_is_audited()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        var version = await WallVersionAsync(layouts, wall);

        var switched = await SwitchAsync(layouts, wall, version, new { target = "next" });

        switched.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(switched));
        var body = await switched.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("showing").GetGuid().ShouldBe(b);
        body.GetProperty("sceneVersion").GetInt64().ShouldBe(1L);

        (Guid Previous, Guid Current, string Cause) audited = await PollForAuditAsync(
            "WallSceneChangedV1", wall,
            "(payload->>'PreviousLayout') || '|' || (payload->>'CurrentLayout') || '|' || (payload->>'Cause')",
            row =>
            {
                var parts = row.Split('|');
                return (Guid.Parse(parts[0]), Guid.Parse(parts[1]), parts[2]);
            });
        audited.Previous.ShouldBe(a);
        audited.Current.ShouldBe(b);
        audited.Cause.ShouldBe("Operator");
    }

    [Fact]
    public async Task US1_4_Jumping_to_a_named_scene_shows_that_scene()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var c = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b, c]);
        var version = await WallVersionAsync(layouts, wall);

        var switched = await SwitchAsync(layouts, wall, version, new { target = "layout", layout = c });

        switched.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(switched));
        (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("showing").GetGuid().ShouldBe(c);
    }

    [Fact]
    public async Task US1_5_Next_wraps_from_the_last_scene_back_to_the_first()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var c = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b, c]);

        await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = c });
        var wrapped = await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "next" });

        wrapped.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(wrapped));
        (await wrapped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("showing").GetGuid().ShouldBe(a);
    }

    [Fact]
    public async Task US1_6_Switching_to_the_scene_already_showing_is_a_no_op()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        var version = await WallVersionAsync(layouts, wall);

        var result = await SwitchAsync(layouts, wall, version, new { target = "layout", layout = a });

        result.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(result));
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sceneVersion").GetInt64().ShouldBe(0L);

        (await CountAuditRowsAsync("WallSceneChangedV1", wall)).ShouldBe(0, "a no-op switch must publish nothing");
    }

    // ---- US1-7 / US1-8 / US1-9: conflicts -------------------------------------

    [Fact]
    public async Task US1_7_A_stale_If_Match_on_switch_is_refused_with_409_WALL_STALE()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        var staleVersion = await WallVersionAsync(layouts, wall);

        // Someone else switches first, moving the version off what we read.
        await SwitchAsync(layouts, wall, staleVersion, new { target = "next" });

        var refused = await SwitchAsync(layouts, wall, staleVersion, new { target = "next" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_STALE");
    }

    [Fact]
    public async Task US1_8_Switching_to_a_scene_whose_layout_is_no_longer_Published_is_refused_with_409()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        await RevertToRevertToDraftAsync(layouts, b);

        var refused = await SwitchAsync(
            layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = b });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()
            .ShouldBe("WALL_SCENE_NOT_PUBLISHED");
    }

    [Fact]
    public async Task US1_9_Creating_a_wall_with_a_name_already_live_in_the_fab_is_refused_with_409()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var name = UniqueName();
        (await CreateWallAsync(layouts, name, [a, b])).StatusCode.ShouldBe(HttpStatusCode.Created);

        var refused = await CreateWallAsync(layouts, name, [a, b]);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_NAME_TAKEN");
    }

    // ---- US1-10: bad scene sets ------------------------------------------------

    [Fact]
    public async Task US1_10_A_single_scene_is_refused_with_400_WALL_TOO_FEW_SCENES()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);

        var refused = await CreateWallAsync(layouts, UniqueName(), [a]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_TOO_FEW_SCENES");
    }

    [Fact]
    public async Task US1_10_Nine_distinct_scenes_are_refused_with_400_WALL_TOO_MANY_SCENES()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var nine = new Guid[9];
        for (var i = 0; i < 9; i++)
        {
            nine[i] = await PublishedLayoutAsync(layouts);
        }

        var refused = await CreateWallAsync(layouts, UniqueName(), nine);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_TOO_MANY_SCENES");
    }

    [Fact]
    public async Task US1_10_A_duplicate_scene_is_refused_with_400_WALL_DUPLICATE_SCENE()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);

        var refused = await CreateWallAsync(layouts, UniqueName(), [a, a]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_DUPLICATE_SCENE");
    }

    [Fact]
    public async Task US1_10_A_scene_from_another_fab_is_refused_with_400_WALL_SCENE_OTHER_FAB()
    {
        using var munich = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(munich);
        var dresdenLayout = await PublishedLayoutAsync(
            await aspire.CreateAuthenticatedClientAsync("layout-composition", MultiFabOperator, MultiFabOperatorPassword),
            "dresden");

        var refused = await CreateWallAsync(munich, UniqueName(), [a, dresdenLayout]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_SCENE_OTHER_FAB");
    }

    [Fact]
    public async Task US1_10_An_unknown_scene_identifier_is_refused_with_400_WALL_SCENE_NOT_FOUND()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);

        var refused = await CreateWallAsync(layouts, UniqueName(), [a, Guid.CreateVersion7()]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_SCENE_NOT_FOUND");
    }

    // ---- US1-11 / US1-12: bad switch requests ---------------------------------

    [Fact]
    public async Task US1_11_Switching_to_a_layout_not_among_the_scenes_is_refused_with_400()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var outside = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b]);

        var refused = await SwitchAsync(
            layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = outside });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_SCENE_NOT_IN_SET");
    }

    [Fact]
    public async Task US1_12_A_switch_without_If_Match_is_refused_with_428()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b]);

        var refused = await layouts.PostAsJsonAsync($"/walls/{wall}/switch", new { target = "next" });

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await BodyAsync(refused));
    }

    // ---- US1-13: kiosk cannot switch, can read --------------------------------

    [Fact]
    public async Task US1_13_A_kiosk_scoped_token_cannot_switch_but_can_read()
    {
        using var admin = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(admin);
        var b = await PublishedLayoutAsync(admin);
        var wall = await CreateWallReturningIdAsync(admin, [a, b]);

        var enrolled = await EnrollKioskAsync();
        var kioskToken = await ClientCredentialsTokenAsync(enrolled.ClientId, enrolled.ClientSecret);
        using var kiosk = aspire.CreateServiceClient("layout-composition");
        kiosk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kioskToken);

        HttpRequestMessage switchRequest = new(HttpMethod.Post, $"/walls/{wall}/switch")
        {
            Content = JsonContent.Create(new { target = "next" }),
        };
        switchRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{await WallVersionAsync(admin, wall)}\"");
        var switchResponse = await kiosk.SendAsync(switchRequest);
        switchResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(switchResponse));

        var read = await kiosk.GetAsync($"/walls/{wall}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(read));
    }

    // ---- US1-14: fab isolation --------------------------------------------------

    [Fact]
    public async Task US1_14_Another_fabs_wall_is_reported_as_not_found()
    {
        using var munich = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(munich);
        var b = await PublishedLayoutAsync(munich);
        var wall = await CreateWallReturningIdAsync(munich, [a, b]);

        using var dresden = await aspire.CreateAuthenticatedClientAsync(
            "layout-composition", DresdenOperator, DresdenOperatorPassword);

        var hidden = await dresden.GetAsync($"/walls/{wall}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(hidden));

        HttpRequestMessage switchRequest = new(HttpMethod.Post, $"/walls/{wall}/switch")
        {
            Content = JsonContent.Create(new { target = "next" }),
        };
        switchRequest.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        var switchRefused = await dresden.SendAsync(switchRequest);
        switchRefused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(switchRefused));
    }

    // ---- US1-15: editing scenes moves the pointer -----------------------------

    [Fact]
    public async Task US1_15_Dropping_the_showing_scene_moves_the_pointer_to_the_new_first_scene()
    {
        using var layouts = await aspire.CreateAdminClientAsync("layout-composition");
        var a = await PublishedLayoutAsync(layouts);
        var b = await PublishedLayoutAsync(layouts);
        var c = await PublishedLayoutAsync(layouts);
        var wall = await CreateWallReturningIdAsync(layouts, [a, b, c]);
        // Move Showing to b so the edit below genuinely drops the current pointer.
        await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = b });

        HttpRequestMessage edit = new(HttpMethod.Put, $"/walls/{wall}/scenes")
        {
            Content = JsonContent.Create(new { scenes = new[] { a, c } }),
        };
        edit.Headers.TryAddWithoutValidation("If-Match", $"\"{await WallVersionAsync(layouts, wall)}\"");
        var edited = await layouts.SendAsync(edit);

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(edited));
        (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("showing").GetGuid().ShouldBe(a);

        // The Operator switch above and this Reconfigured edit both write a
        // WallSceneChangedV1 audit row for this wall, independently, via the
        // outbox — so "latest by occurred_at" races with which delivery lands
        // first. Filter on Cause so the poll waits for the row it actually
        // wants instead of grabbing whichever of the two exists first.
        (Guid Previous, Guid Current, string Cause) audited = await PollForAuditAsync(
            "WallSceneChangedV1", wall,
            "(payload->>'PreviousLayout') || '|' || (payload->>'CurrentLayout') || '|' || (payload->>'Cause')",
            row =>
            {
                var parts = row.Split('|');
                return (Guid.Parse(parts[0]), Guid.Parse(parts[1]), parts[2]);
            },
            extraWhereClause: "payload->>'Cause' = 'Reconfigured'");
        audited.Cause.ShouldBe("Reconfigured");
    }

    // ---- helpers ---------------------------------------------------------------

    private async Task<Guid> PublishedLayoutAsync(HttpClient layouts, string fab = "munich")
    {
        var name = $"W-{Guid.NewGuid():N}"[..16];
        var camera = await LayoutRequests.RegisterCameraAsync(aspire, fab);

        var created = await layouts.PostAsJsonAsync("/layouts" + (fab == "munich" ? "" : $"?fabId={fab}"), new
        {
            name,
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 },
            },
        });
        created.EnsureSuccessStatusCode();
        var layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        var published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        return layoutIdentifier;
    }

    private static async Task RevertToRevertToDraftAsync(HttpClient layouts, Guid layoutIdentifier)
    {
        var reverted = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/revert");
        reverted.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Enrols a genuine kiosk service identity via the real
    /// <c>POST /kiosks/enroll</c> surface (mirrors
    /// <c>CrossFabDisableIntegrationTests.EnrollKioskAsync</c>), rather than a
    /// password grant for a human user against the <c>kiosk-web</c> SPA client
    /// — that client is public and has no direct-access-grant, so Keycloak
    /// refuses it outright, and even granted it would be a human's token, not
    /// a kiosk-scoped one.
    /// </summary>
    private async Task<(string ClientId, string ClientSecret)> EnrollKioskAsync()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = $"t258-wall-{Guid.CreateVersion7():N}";

        var enrolled = await identity.PostAsJsonAsync(
            "/kiosks/enroll?fabId=munich", new { name });
        enrolled.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(enrolled));

        var body = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }

    /// <summary>
    /// The <c>client_credentials</c> grant for the just-enrolled kiosk,
    /// hand-rolled the same way <c>PlantFloor.SimulatorTokenAsync</c> and
    /// <c>WebhookBearerValidationIntegrationTests.ScopeLessServiceAccountTokenAsync</c>
    /// do (ADR-0036), rather than added to <see cref="AspireFixture"/>: a third
    /// call site is not yet a pattern worth collapsing in a bug-fix change.
    /// </summary>
    private async Task<string> ClientCredentialsTokenAsync(string clientId, string clientSecret)
    {
        using var keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        var token = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        token.EnsureSuccessStatusCode();

        return (await token.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    private static Task<HttpResponseMessage> CreateWallAsync(
        HttpClient layouts, string name, IReadOnlyList<Guid> scenes, string? key = null)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/walls")
        {
            Content = JsonContent.Create(new { name, scenes }),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }
        return layouts.SendAsync(request);
    }

    private static async Task<Guid> CreateWallReturningIdAsync(HttpClient layouts, IReadOnlyList<Guid> scenes)
    {
        var created = await CreateWallAsync(layouts, UniqueName(), scenes);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<int> WallVersionAsync(HttpClient layouts, Guid wall)
    {
        var fetched = await layouts.GetAsync($"/walls/{wall}");
        fetched.EnsureSuccessStatusCode();
        return (await fetched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
    }

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient layouts, Guid wall, int version, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/walls/{wall}/switch")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return layouts.SendAsync(request);
    }

    private static async Task<string[]> ListNamesAsync(HttpClient layouts)
    {
        var listed = await layouts.GetAsync("/walls");
        listed.EnsureSuccessStatusCode();
        var page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];
    }

    private async Task<T> PollForAuditAsync<T>(
        string eventKind, Guid wall, string selectExpression, Func<string, T> parse, string? extraWhereClause = null)
    {
        // selectExpression (and extraWhereClause) are hardcoded, test-authored
        // SQL fragments (never user input), so they are spliced into the SQL
        // text directly. Splicing them through SqlQuery's FormattableString
        // hole instead would bind them as literal string VALUEs rather than
        // raw SQL — every row's "Value" would come back as the C# expression's
        // own text. eventKind/wall stay genuine bound parameters via
        // SqlQueryRaw's positional {0}/{1} placeholders, the same mechanism
        // DeadLetterReasonIntegrationTests uses for its LIKE parameter.
        //
        // extraWhereClause narrows the poll to a specific row when a single
        // wall can legitimately produce more than one matching audit row
        // (US1-15: an Operator switch and a Reconfigured edit both write a
        // WallSceneChangedV1 for the same wall) — "latest by occurred_at" is
        // racy there, since both rows land via independent async outbox
        // deliveries whose completion order need not match issue order.
        var extraWhere = extraWhereClause is null ? string.Empty : $"AND {extraWhereClause}";
        var sql = $$"""
            SELECT {{selectExpression}} AS "Value"
            FROM audit_events
            WHERE event_kind = {0}
              AND payload->>'Wall' = {1}
              {{extraWhere}}
            ORDER BY occurred_at DESC
            """;

        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

            var rows = await context.Database
                .SqlQueryRaw<string>(sql, eventKind, wall.ToString())
                .ToListAsync();

            if (rows.Count > 0)
            {
                return parse(rows[0]);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No {eventKind} audit row for wall {wall} within 30s.");
    }

    private async Task<int> CountAuditRowsAsync(string eventKind, Guid wall)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

        var rows = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = {eventKind}
                  AND payload->>'Wall' = {wall.ToString()}
                """)
            .ToListAsync();

        return rows[0];
    }

    private static string UniqueName() => $"Wall-{Guid.NewGuid():N}"[..16];

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}";
}
