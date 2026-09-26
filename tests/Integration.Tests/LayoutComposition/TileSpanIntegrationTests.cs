using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 258 (issue #2607, ADR-0156) — the US1 Gherkin in <c>spec.md</c> §2, one
/// <c>[Fact]</c> per scenario, against the real Aspire stack (ADR-0103).
///
/// <para>
/// <b>Phase 4a, red-first (ADR-0139/0144, spec §8).</b> On unmodified
/// production code every scenario here is red on its own assertion, except
/// the three declared pins: <see cref="Anonymous_POST_returns_401"/>,
/// <see cref="A_caller_without_sse_layouts_write_is_refused_403"/>, and the
/// grid-over-nine-cells refusals
/// (<see cref="A_2x5_grid_is_refused_as_LAYOUT_INVALID_INPUT"/>,
/// <see cref="A_4x3_grid_is_refused_as_LAYOUT_INVALID_INPUT"/>) — all four are
/// existing behaviour, unchanged by this feature, so their green result is
/// characterisation, not 4a evidence.
/// </para>
///
/// <para>
/// Two scenarios that *look* like they could pass today must not:
/// <see cref="Omitted_spans_mean_1x1"/> reads <c>rowSpan</c>/<c>colSpan</c>
/// straight off the GET response, which <c>TileDto</c> does not carry yet — a
/// green result here would mean the assertion is not reading the field.
/// <see cref="Two_1x1_tiles_sharing_an_origin_are_refused_as_LAYOUT_TILE_OVERLAP"/>
/// (the ADR-0112 duplicate-position case) asserts the *new* problem title;
/// today's code still answers <c>LAYOUT_TILE_POSITION_DUPLICATE</c>.
/// </para>
///
/// <para>
/// <see cref="A_stale_If_Match_on_the_hero_wall_is_refused_and_keeps_its_spans"/>
/// is red today for a different reason than the rest: its own setup (a
/// published 3×3 hero wall) cannot be created until the cap and span support
/// land, so the test never reaches its 409 assertion. It is not a pin.
/// </para>
///
/// <para>
/// Anonymous-object request bodies throughout (plan §6.2 / tasks T004): they
/// compile today because <c>System.Text.Json</c> silently ignores the extra
/// <c>rowSpan</c>/<c>colSpan</c> properties against today's <c>TileRequest</c>,
/// which has no such members.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class TileSpanIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetLayoutCompositionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_hero_and_thumbnails_wall_is_accepted_and_its_spans_are_read_back()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(6);

        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", HeroWallBody(UniqueName(), cameras));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        JsonElement revision = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0];
        revision.GetProperty("gridRows").GetInt32().ShouldBe(3);
        revision.GetProperty("gridCols").GetInt32().ShouldBe(3);

        JsonElement tiles = revision.GetProperty("tiles");
        JsonElement hero = tiles.EnumerateArray().Single(
            tile => tile.GetProperty("row").GetInt32() == 0 && tile.GetProperty("col").GetInt32() == 0);
        hero.GetProperty("rowSpan").GetInt32().ShouldBe(2);
        hero.GetProperty("colSpan").GetInt32().ShouldBe(2);

        foreach (JsonElement tile in tiles.EnumerateArray())
        {
            if (tile.GetProperty("row").GetInt32() == 0 && tile.GetProperty("col").GetInt32() == 0)
            {
                continue;
            }

            tile.GetProperty("rowSpan").GetInt32().ShouldBe(1);
            tile.GetProperty("colSpan").GetInt32().ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_full_3x3_of_single_tiles_is_accepted_at_the_new_cap()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(9);

        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", FullGridBody(UniqueName(), cameras));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
    }

    /// <summary>
    /// The trap (spec §8): must be red today, because <c>TileDto</c> carries
    /// no <c>rowSpan</c>/<c>colSpan</c> yet, so the properties this reads off
    /// the GET response do not exist.
    /// </summary>
    [Fact]
    public async Task Omitted_spans_mean_1x1()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire);

        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        JsonElement tile = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0].GetProperty("tiles")[0];

        tile.GetProperty("rowSpan").GetInt32().ShouldBe(1);
        tile.GetProperty("colSpan").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Publishing_the_hero_wall_carries_its_spans_on_the_audited_LayoutRevisionPublishedV2()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(6);
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", HeroWallBody(UniqueName(), cameras));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(published));

        using HttpClient auditReader = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", "operator", "Operator1234");
        JsonElement row = await PollForPublishedAuditRowAsync(auditReader, layoutIdentifier);

        JsonElement payload = JsonDocument.Parse(row.GetProperty("payload").GetString()!).RootElement;
        JsonElement hero = payload.GetProperty("tiles").EnumerateArray().Single(
            tile => tile.GetProperty("row").GetInt32() == 0 && tile.GetProperty("col").GetInt32() == 0);
        hero.GetProperty("rowSpan").GetInt32().ShouldBe(2);
        hero.GetProperty("colSpan").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Branching_a_published_hero_wall_keeps_its_spans()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(6);
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", HeroWallBody(UniqueName(), cameras));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        (await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage branched = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "draft");
        branched.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(branched));

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        JsonElement revisions = (await fetched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revisions");
        JsonElement draftRevision = revisions.EnumerateArray()
            .Single(revision => revision.GetProperty("revisionNumber").GetInt32() == 2);
        JsonElement hero = draftRevision.GetProperty("tiles").EnumerateArray().Single(
            tile => tile.GetProperty("row").GetInt32() == 0 && tile.GetProperty("col").GetInt32() == 0);
        hero.GetProperty("rowSpan").GetInt32().ShouldBe(2);
        hero.GetProperty("colSpan").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// Plan §3.5 / tasks T004: a row inserted the way it existed before the
    /// migration — no <c>row_span</c>/<c>col_span</c> named at all — must read
    /// back as 1×1. The raw-SQL recipe mirrors
    /// <c>CameraCatalog/StaleIdempotencyReservationIntegrationTests.cs</c>:
    /// every row here comes from damaging/extending a real write, never a
    /// hand-invented one.
    /// </summary>
    [Fact]
    public async Task A_tile_row_written_before_the_migration_reads_as_1x1()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid firstCamera = await LayoutRequests.RegisterCameraAsync(aspire);
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 1, cols = 2 },
                tiles = new[]
                {
                    new { cameraIdentifier = firstCamera, overlayIdentifier = (Guid?)null, row = 0, col = 0 },
                },
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        Guid secondCamera = await LayoutRequests.RegisterCameraAsync(aspire);
        await InsertRawTileNamingNoSpanColumnAsync(layoutIdentifier, secondCamera, row: 0, col: 1);

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        fetched.EnsureSuccessStatusCode();
        JsonElement tiles = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0].GetProperty("tiles");
        JsonElement rawTile = tiles.EnumerateArray().Single(tile => tile.GetProperty("col").GetInt32() == 1);

        rawTile.GetProperty("rowSpan").GetInt32().ShouldBe(1);
        rawTile.GetProperty("colSpan").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Two_intersecting_spans_are_refused_as_LAYOUT_TILE_OVERLAP()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(2);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 3, cols = 3 },
                tiles = new[]
                {
                    Tile(cameras[0], 0, 0, rowSpan: 2, colSpan: 2),
                    Tile(cameras[1], 1, 1),
                },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_TILE_OVERLAP");
    }

    /// <summary>
    /// The trap (spec §8): the ADR-0112 duplicate-position case is the 1×1
    /// instance of overlap. Must be red today — the code is still
    /// <c>LAYOUT_TILE_POSITION_DUPLICATE</c>.
    /// </summary>
    [Fact]
    public async Task Two_1x1_tiles_sharing_an_origin_are_refused_as_LAYOUT_TILE_OVERLAP()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(2);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 2, cols = 2 },
                tiles = new[]
                {
                    Tile(cameras[0], 0, 0),
                    Tile(cameras[1], 0, 0),
                },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_TILE_OVERLAP");
    }

    [Fact]
    public async Task A_span_that_runs_off_the_grid_is_refused_as_LAYOUT_TILE_OUT_OF_BOUNDS()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 3, cols = 3 },
                tiles = new[] { Tile(camera, 1, 1, rowSpan: 1, colSpan: 3) },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_TILE_OUT_OF_BOUNDS");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    public async Task A_zero_or_negative_span_is_refused_as_LAYOUT_INVALID_INPUT(int rowSpan, int colSpan)
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { Tile(camera, 0, 0, rowSpan, colSpan) },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
    }

    /// <summary>
    /// Declared pin (spec §8): a grid over nine cells is refused today too —
    /// 2×5 is 10 cells, over even today's 4-cell cap. Green here is
    /// characterisation, not 4a evidence.
    /// </summary>
    [Fact]
    public async Task A_2x5_grid_is_refused_as_LAYOUT_INVALID_INPUT()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 2, cols = 5 },
                tiles = new[] { Tile(camera, 0, 0) },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
    }

    /// <summary>Declared pin (spec §8) — see <see cref="A_2x5_grid_is_refused_as_LAYOUT_INVALID_INPUT"/>.</summary>
    [Fact]
    public async Task A_4x3_grid_is_refused_as_LAYOUT_INVALID_INPUT()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 4, cols = 3 },
                tiles = new[] { Tile(camera, 0, 0) },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(response));
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("LAYOUT_INVALID_INPUT");
    }

    /// <summary>
    /// Red today, but NOT a pin (spec §8): the setup itself — a published 3×3
    /// hero wall — cannot be created before the cap and span support land, so
    /// this never reaches its 409 assertion. The status is 409 (Conflict), not
    /// 412 (PreconditionFailed): <c>EditDraftRevisionErrors.LayoutRevisionStale</c>
    /// maps to <c>HttpStatusCode.Conflict</c>, unlike <c>CameraCatalog</c>'s
    /// stale-version errors (ADR-0119's 412 convention) — a pre-existing,
    /// unrelated divergence, tracked by a follow-up issue rather than changed here.
    /// </summary>
    [Fact]
    public async Task A_stale_If_Match_on_the_hero_wall_is_refused_and_keeps_its_spans()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid[] cameras = await RegisterCamerasAsync(6);
        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts", HeroWallBody(UniqueName(), cameras));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpRequestMessage patch = new(HttpMethod.Patch, $"/layouts/{layoutIdentifier}/revisions/1")
        {
            Content = JsonContent.Create(new
            {
                grid = new { rows = 3, cols = 3 },
                tiles = new[] { Tile(cameras[0], 0, 0, rowSpan: 2, colSpan: 2) },
            }),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", "\"999\"");
        HttpResponseMessage response = await layouts.SendAsync(patch);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(response));

        HttpResponseMessage fetched = await layouts.GetAsync($"/layouts/{layoutIdentifier}");
        JsonElement hero = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0].GetProperty("tiles").EnumerateArray().Single(
                tile => tile.GetProperty("row").GetInt32() == 0 && tile.GetProperty("col").GetInt32() == 0);
        hero.GetProperty("rowSpan").GetInt32().ShouldBe(2);
        hero.GetProperty("colSpan").GetInt32().ShouldBe(2);
    }

    /// <summary>Declared pin (spec §8) — authorization is unchanged.</summary>
    [Fact]
    public async Task Anonymous_POST_returns_401()
    {
        HttpResponseMessage response = await aspire.LayoutComposition.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { Tile(Guid.CreateVersion7(), 0, 0) },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Declared pin (spec §8) — authorization is unchanged.
    /// <c>smart-sentinel-eye-web</c> is a real Keycloak client that grants no
    /// <c>sse.layouts.write</c> (nor any <c>sse.layouts.*</c>) by default,
    /// mirroring <c>ConsoleScopeGrantIntegrationTests</c>.
    /// </summary>
    [Fact]
    public async Task A_caller_without_sse_layouts_write_is_refused_403()
    {
        string token = await aspire.GetAccessTokenForClientAsync(
            "smart-sentinel-eye-web", AspireFixture.AdminUsername, AspireFixture.AdminPassword, "openid");
        using HttpClient layouts = aspire.CreateServiceClient("layout-composition");
        layouts.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await layouts.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = UniqueName(),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { Tile(Guid.CreateVersion7(), 0, 0) },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(response));
    }

    // ---- helpers --------------------------------------------------------

    private async Task<Guid[]> RegisterCamerasAsync(int count)
    {
        Guid[] cameras = new Guid[count];
        for (int i = 0; i < count; i++)
        {
            cameras[i] = await LayoutRequests.RegisterCameraAsync(aspire);
        }

        return cameras;
    }

    private static object HeroWallBody(string name, Guid[] cameras) => new
    {
        name,
        grid = new { rows = 3, cols = 3 },
        tiles = new[]
        {
            Tile(cameras[0], 0, 0, rowSpan: 2, colSpan: 2),
            Tile(cameras[1], 0, 2),
            Tile(cameras[2], 1, 2),
            Tile(cameras[3], 2, 0),
            Tile(cameras[4], 2, 1),
            Tile(cameras[5], 2, 2),
        },
    };

    private static object FullGridBody(string name, Guid[] cameras)
    {
        List<object> tiles = [];
        int index = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                tiles.Add(Tile(cameras[index], row, col));
                index++;
            }
        }

        return new { name, grid = new { rows = 3, cols = 3 }, tiles };
    }

    private static object Tile(Guid camera, int row, int col, int rowSpan = 1, int colSpan = 1) => new
    {
        cameraIdentifier = camera,
        overlayIdentifier = (Guid?)null,
        row,
        col,
        rowSpan,
        colSpan,
    };

    private static string UniqueName() => $"Span-{Guid.NewGuid():N}"[..16];

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}";

    private static async Task<JsonElement> PollForPublishedAuditRowAsync(HttpClient auditReader, Guid layoutIdentifier)
    {
        string query = $"/audit?eventKind=LayoutRevisionPublishedV2&resourceIdentifier={layoutIdentifier}&pageSize=10";

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage response = await auditReader.GetAsync(query);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            JsonElement rows = page.GetProperty("rows");
            if (rows.GetArrayLength() >= 1)
            {
                return rows[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No audit row for LayoutRevisionPublishedV2 / {layoutIdentifier} appeared within 20s.");
    }

    /// <summary>
    /// Mirrors <c>CameraCatalog/StaleIdempotencyReservationIntegrationTests</c>'s
    /// recipe: extends a real revision row with a raw SQL insert that names
    /// only the columns that existed before this feature's migration, so the
    /// database default (once the migration lands) is what supplies the span.
    /// </summary>
    private async Task InsertRawTileNamingNoSpanColumnAsync(Guid layoutIdentifier, Guid camera, int row, int col)
    {
        await using LayoutCompositionDbContext db = await aspire.CreateLayoutCompositionDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO layout_revision_tiles (revision_id, row, col, camera_id, overlay_id)
            SELECT revision_id, {1}, {2}, {3}, NULL
              FROM layout_revisions
             WHERE layout_id = {0} AND revision_number = 1;
            """,
            layoutIdentifier, row, col, camera);
    }
}
