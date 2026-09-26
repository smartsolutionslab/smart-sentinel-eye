using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
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
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetLayoutCompositionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- US1-1 / US1-2: create -----------------------------------------------

    [Fact]
    public async Task US1_1_Creating_a_wall_with_two_Published_scenes_succeeds_and_is_audited()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        string name = UniqueName();

        HttpResponseMessage created = await CreateWallAsync(layouts, name, [a, b], key: $"key-{Guid.CreateVersion7():N}");
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid wall = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage fetched = await layouts.GetAsync($"/walls/{wall}");
        fetched.EnsureSuccessStatusCode();
        JsonElement body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("scenes").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([a, b]);
        body.GetProperty("showing").GetGuid().ShouldBe(a);
        body.GetProperty("sceneVersion").GetInt64().ShouldBe(0L);
        fetched.Headers.ETag.ShouldNotBeNull();

        (Guid Wall, string Name) audited = await PollForAuditAsync<(Guid, string)>(
            "WallConfiguredV1", wall,
            "wall_identifier::text || '|' || (payload->>'Name')",
            row => (Guid.Parse(row.Split('|')[0]), row.Split('|')[1]));
        audited.Name.ShouldBe(name);
    }

    [Fact]
    public async Task US1_2_A_replayed_create_with_the_same_idempotency_key_returns_the_original_wall()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        string name = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage first = await CreateWallAsync(layouts, name, [a, b], key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(first));
        Guid firstId = await first.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage second = await CreateWallAsync(layouts, name, [a, b], key);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(second));
        Guid secondId = await second.Content.ReadFromJsonAsync<Guid>();

        secondId.ShouldBe(firstId, "a replay must return the wall the first attempt created, not a second one");
        (await ListNamesAsync(layouts)).Count(n => n == name).ShouldBe(1);
    }

    // ---- US1-3 / US1-4 / US1-5 / US1-6: switching -----------------------------

    [Fact]
    public async Task US1_3_A_manual_next_switch_advances_the_scene_and_is_audited()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        int version = await WallVersionAsync(layouts, wall);

        HttpResponseMessage switched = await SwitchAsync(layouts, wall, version, new { target = "next" });

        switched.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(switched));
        JsonElement body = await switched.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("showing").GetGuid().ShouldBe(b);
        body.GetProperty("sceneVersion").GetInt64().ShouldBe(1L);

        (Guid Previous, Guid Current, string Cause) audited = await PollForAuditAsync(
            "WallSceneChangedV1", wall,
            "(payload->>'PreviousLayout') || '|' || (payload->>'CurrentLayout') || '|' || (payload->>'Cause')",
            row =>
            {
                string[] parts = row.Split('|');
                return (Guid.Parse(parts[0]), Guid.Parse(parts[1]), parts[2]);
            });
        audited.Previous.ShouldBe(a);
        audited.Current.ShouldBe(b);
        audited.Cause.ShouldBe("Operator");
    }

    [Fact]
    public async Task US1_4_Jumping_to_a_named_scene_shows_that_scene()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid c = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b, c]);
        int version = await WallVersionAsync(layouts, wall);

        HttpResponseMessage switched = await SwitchAsync(layouts, wall, version, new { target = "layout", layout = c });

        switched.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(switched));
        (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("showing").GetGuid().ShouldBe(c);
    }

    [Fact]
    public async Task US1_5_Next_wraps_from_the_last_scene_back_to_the_first()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid c = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b, c]);

        await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = c });
        HttpResponseMessage wrapped = await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "next" });

        wrapped.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(wrapped));
        (await wrapped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("showing").GetGuid().ShouldBe(a);
    }

    [Fact]
    public async Task US1_6_Switching_to_the_scene_already_showing_is_a_no_op()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        int version = await WallVersionAsync(layouts, wall);

        HttpResponseMessage result = await SwitchAsync(layouts, wall, version, new { target = "layout", layout = a });

        result.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(result));
        JsonElement body = await result.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sceneVersion").GetInt64().ShouldBe(0L);

        (await CountAuditRowsAsync("WallSceneChangedV1", wall)).ShouldBe(0, "a no-op switch must publish nothing");
    }

    // ---- US1-7 / US1-8 / US1-9: conflicts -------------------------------------

    [Fact]
    public async Task US1_7_A_stale_If_Match_on_switch_is_refused_with_409_WALL_STALE()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        int staleVersion = await WallVersionAsync(layouts, wall);

        // Someone else switches first, moving the version off what we read.
        await SwitchAsync(layouts, wall, staleVersion, new { target = "next" });

        HttpResponseMessage refused = await SwitchAsync(layouts, wall, staleVersion, new { target = "next" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_STALE");
    }

    [Fact]
    public async Task US1_8_Switching_to_a_scene_whose_layout_is_no_longer_Published_is_refused_with_409()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        await RevertToRevertToDraftAsync(layouts, b);

        HttpResponseMessage refused = await SwitchAsync(
            layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = b });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()
            .ShouldBe("WALL_SCENE_NOT_PUBLISHED");
    }

    [Fact]
    public async Task US1_9_Creating_a_wall_with_a_name_already_live_in_the_fab_is_refused_with_409()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        string name = UniqueName();
        (await CreateWallAsync(layouts, name, [a, b])).StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage refused = await CreateWallAsync(layouts, name, [a, b]);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_NAME_TAKEN");
    }

    // ---- US1-10: bad scene sets ------------------------------------------------

    [Fact]
    public async Task US1_10_A_single_scene_is_refused_with_400_WALL_TOO_FEW_SCENES()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);

        HttpResponseMessage refused = await CreateWallAsync(layouts, UniqueName(), [a]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_TOO_FEW_SCENES");
    }

    [Fact]
    public async Task US1_10_Nine_distinct_scenes_are_refused_with_400_WALL_TOO_MANY_SCENES()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] nine = new Guid[9];
        for (int i = 0; i < 9; i++)
        {
            nine[i] = await PublishedLayoutAsync(layouts);
        }

        HttpResponseMessage refused = await CreateWallAsync(layouts, UniqueName(), nine);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_TOO_MANY_SCENES");
    }

    [Fact]
    public async Task US1_10_A_duplicate_scene_is_refused_with_400_WALL_DUPLICATE_SCENE()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);

        HttpResponseMessage refused = await CreateWallAsync(layouts, UniqueName(), [a, a]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_DUPLICATE_SCENE");
    }

    [Fact]
    public async Task US1_10_A_scene_from_another_fab_is_refused_with_400_WALL_SCENE_OTHER_FAB()
    {
        using HttpClient munich = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(munich);
        Guid dresdenLayout = await PublishedLayoutAsync(
            await aspire.CreateAuthenticatedClientAsync("layout-composition", MultiFabOperator, OperatorPassword),
            "dresden");

        HttpResponseMessage refused = await CreateWallAsync(munich, UniqueName(), [a, dresdenLayout]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_SCENE_OTHER_FAB");
    }

    [Fact]
    public async Task US1_10_An_unknown_scene_identifier_is_refused_with_400_WALL_SCENE_NOT_FOUND()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);

        HttpResponseMessage refused = await CreateWallAsync(layouts, UniqueName(), [a, Guid.CreateVersion7()]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_SCENE_NOT_FOUND");
    }

    // ---- US1-11 / US1-12: bad switch requests ---------------------------------

    [Fact]
    public async Task US1_11_Switching_to_a_layout_not_among_the_scenes_is_refused_with_400()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid outside = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);

        HttpResponseMessage refused = await SwitchAsync(
            layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = outside });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().ShouldBe("WALL_SCENE_NOT_IN_SET");
    }

    [Fact]
    public async Task US1_12_A_switch_without_If_Match_is_refused_with_428()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);

        HttpResponseMessage refused = await layouts.PostAsJsonAsync($"/walls/{wall}/switch", new { target = "next" });

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await BodyAsync(refused));
    }

    // ---- US1-13: kiosk cannot switch, can read --------------------------------

    [Fact]
    public async Task US1_13_A_kiosk_scoped_token_cannot_switch_but_can_read()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(admin);
        Guid b = await PublishedLayoutAsync(admin);
        Guid wall = await CreateWallReturningIdAsync(admin, [a, b]);

        string kioskToken = await aspire.GetAccessTokenForClientAsync("kiosk-web", "admin", "Admin1234", "openid");
        using HttpClient kiosk = aspire.CreateServiceClient("layout-composition");
        kiosk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kioskToken);

        HttpRequestMessage switchRequest = new(HttpMethod.Post, $"/walls/{wall}/switch")
        {
            Content = JsonContent.Create(new { target = "next" }),
        };
        switchRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{await WallVersionAsync(admin, wall)}\"");
        HttpResponseMessage switchResponse = await kiosk.SendAsync(switchRequest);
        switchResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(switchResponse));

        HttpResponseMessage read = await kiosk.GetAsync($"/walls/{wall}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(read));
    }

    // ---- US1-14: fab isolation --------------------------------------------------

    [Fact]
    public async Task US1_14_Another_fabs_wall_is_reported_as_not_found()
    {
        using HttpClient munich = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(munich);
        Guid b = await PublishedLayoutAsync(munich);
        Guid wall = await CreateWallReturningIdAsync(munich, [a, b]);

        using HttpClient dresden = await aspire.CreateAuthenticatedClientAsync(
            "layout-composition", DresdenOperator, OperatorPassword);

        HttpResponseMessage hidden = await dresden.GetAsync($"/walls/{wall}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(hidden));

        HttpRequestMessage switchRequest = new(HttpMethod.Post, $"/walls/{wall}/switch")
        {
            Content = JsonContent.Create(new { target = "next" }),
        };
        switchRequest.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        HttpResponseMessage switchRefused = await dresden.SendAsync(switchRequest);
        switchRefused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(switchRefused));
    }

    // ---- US1-15: editing scenes moves the pointer -----------------------------

    [Fact]
    public async Task US1_15_Dropping_the_showing_scene_moves_the_pointer_to_the_new_first_scene()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid c = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b, c]);
        // Move Showing to b so the edit below genuinely drops the current pointer.
        await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = b });

        HttpRequestMessage edit = new(HttpMethod.Put, $"/walls/{wall}/scenes")
        {
            Content = JsonContent.Create(new { scenes = new[] { a, c } }),
        };
        edit.Headers.TryAddWithoutValidation("If-Match", $"\"{await WallVersionAsync(layouts, wall)}\"");
        HttpResponseMessage edited = await layouts.SendAsync(edit);

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(edited));
        (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("showing").GetGuid().ShouldBe(a);

        (Guid Previous, Guid Current, string Cause) audited = await PollForAuditAsync(
            "WallSceneChangedV1", wall,
            "(payload->>'PreviousLayout') || '|' || (payload->>'CurrentLayout') || '|' || (payload->>'Cause')",
            row =>
            {
                string[] parts = row.Split('|');
                return (Guid.Parse(parts[0]), Guid.Parse(parts[1]), parts[2]);
            });
        audited.Cause.ShouldBe("Reconfigured");
    }

    // ---- helpers ---------------------------------------------------------------

    private async Task<Guid> PublishedLayoutAsync(HttpClient layouts, string fab = "munich")
    {
        string name = $"W-{Guid.NewGuid():N}"[..16];
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, fab);

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts" + (fab == "munich" ? "" : $"?fabId={fab}"), new
        {
            name,
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 },
            },
        });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        return layoutIdentifier;
    }

    private static async Task RevertToRevertToDraftAsync(HttpClient layouts, Guid layoutIdentifier)
    {
        HttpResponseMessage reverted = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/revert");
        reverted.EnsureSuccessStatusCode();
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
        HttpResponseMessage created = await CreateWallAsync(layouts, UniqueName(), scenes);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<int> WallVersionAsync(HttpClient layouts, Guid wall)
    {
        HttpResponseMessage fetched = await layouts.GetAsync($"/walls/{wall}");
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
        HttpResponseMessage listed = await layouts.GetAsync("/walls");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.EnumerateArray().Select(row => row.GetProperty("name").GetString()!)];
    }

    private async Task<T> PollForAuditAsync<T>(
        string eventKind, Guid wall, string selectExpression, Func<string, T> parse)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();

            List<string> rows = await context.Database
                .SqlQuery<string>($"""
                    SELECT {selectExpression} AS "Value"
                    FROM audit_events
                    WHERE event_kind = {eventKind}
                      AND payload->>'Wall' = {wall.ToString()}
                    ORDER BY occurred_at DESC
                    """)
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
        await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> rows = await context.Database
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
