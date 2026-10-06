using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// Spec 302 (#2424/#2492) — one fact per remaining call site/context not
/// already covered by <c>Identity</c>, <c>CameraCatalog</c> or
/// <c>EventIngestion</c>'s own idempotency-reuse suites: Automation
/// (<c>POST /rules</c>), LayoutComposition (<c>POST /layouts</c>,
/// <c>POST /walls</c>), OverlayDesigner (<c>POST /overlays</c> — the one
/// fab-less call site, proving <see cref="Option{T}.None"/> still works) and
/// SystemVariables (<c>POST /system-variables</c>). Together with the other
/// three suites this hits all twelve call sites and all seven contexts'
/// migrations.
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyKeyReuseAcrossContextsIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    [Fact]
    public async Task A_key_reused_for_a_different_rule_is_refused()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        string first = UniqueName();
        string second = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await rules.SendAsync(RuleRequest(first, key));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync("automation", created));

        HttpResponseMessage refused = await rules.SendAsync(RuleRequest(second, key));

        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first rule must not be honoured for a different one: "
            + await DiagnoseAsync("automation", refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        HttpResponseMessage read = await rules.GetAsync($"/rules/{second}");
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the second rule must never have been created");
    }

    [Fact]
    public async Task A_key_reused_for_a_different_layout_is_refused()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, Fab);
        string first = UniqueName();
        string second = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await layouts.SendAsync(LayoutRequest(first, camera, key));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync("layout-composition", created));

        HttpResponseMessage refused = await layouts.SendAsync(LayoutRequest(second, camera, key));

        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first layout must not be honoured for a different one: "
            + await DiagnoseAsync("layout-composition", refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        (await CountLayoutsNamedAsync(second)).ShouldBe(
            0, "the second layout must never have been created by the refused request");
    }

    [Fact]
    public async Task A_key_reused_for_a_different_wall_is_refused()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        Guid sceneA = await PublishedLayoutAsync(layouts);
        Guid sceneB = await PublishedLayoutAsync(layouts);
        string first = UniqueName();
        string second = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await layouts.SendAsync(WallRequest(first, [sceneA, sceneB], key));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync("layout-composition", created));

        HttpResponseMessage refused = await layouts.SendAsync(WallRequest(second, [sceneA, sceneB], key));

        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first wall must not be honoured for a different one: "
            + await DiagnoseAsync("layout-composition", refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        (await CountWallsNamedAsync(second)).ShouldBe(
            0, "the second wall must never have been created by the refused request");
    }

    /// <summary>The one fab-less call site: proves <see cref="Option{T}.None"/> is still handled correctly.</summary>
    [Fact]
    public async Task A_key_reused_for_a_different_overlay_is_refused()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        string first = UniqueName();
        string second = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await overlays.SendAsync(OverlayRequest(first, key));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync("overlay-designer", created));

        HttpResponseMessage refused = await overlays.SendAsync(OverlayRequest(second, key));

        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first overlay must not be honoured for a different one: "
            + await DiagnoseAsync("overlay-designer", refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        (await CountOverlaysNamedAsync(second)).ShouldBe(
            0, "the second overlay must never have been created by the refused request");
    }

    [Fact]
    public async Task A_key_reused_for_a_different_system_variable_is_refused()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string first = UniqueVariableName();
        string second = UniqueVariableName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await variables.SendAsync(VariableRequest(first, key));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync("system-variables", created));

        HttpResponseMessage refused = await variables.SendAsync(VariableRequest(second, key));

        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first variable must not be honoured for a different one: "
            + await DiagnoseAsync("system-variables", refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        HttpResponseMessage read = await variables.GetAsync($"/system-variables/{second}");
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the second variable must never have been created");
    }

    // ---- request builders ---------------------------------------------------

    private static HttpRequestMessage RuleRequest(string name, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/rules")
        {
            Content = JsonContent.Create(new
            {
                name,
                triggerSource = "plc",
                triggerKind = "PlcCycleStart",
                predicate = "$.payload.cycleTime <= 30",
                actionType = "SetVariableValue",
                variableName = "oeeLine1",
                valueExpression = "100 - $.payload.cycleTime * 2",
                overlayIdentifier = (Guid?)null,
                durationMs = (int?)null,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static HttpRequestMessage LayoutRequest(string name, Guid camera, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/layouts")
        {
            Content = JsonContent.Create(new
            {
                name,
                grid = new { rows = 1, cols = 1 },
                tiles = new[]
                {
                    new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 },
                },
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static HttpRequestMessage WallRequest(string name, IReadOnlyList<Guid> scenes, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/walls")
        {
            Content = JsonContent.Create(new { name, scenes }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static HttpRequestMessage OverlayRequest(string name, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/overlays")
        {
            Content = JsonContent.Create(new { name, elements = new[] { SampleLabelBody() } }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static HttpRequestMessage VariableRequest(string name, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/system-variables")
        {
            Content = JsonContent.Create(new
            {
                name,
                type = "Number",
                initialValue = "1",
                truthyLabel = (string?)null,
                falsyLabel = (string?)null,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static object SampleLabelBody() => new
    {
        kind = "Text",
        color = "#FFFFFFD9",
        text = "Spec 302",
        normalizedX = 0.5m,
        normalizedY = 0.05m,
        normalizedWidth = 0.3m,
        normalizedHeight = 0.08m,
        fontSizePx = 48,
    };

    /// <summary>Creates and publishes a one-tile layout, for a wall's scene list.</summary>
    private async Task<Guid> PublishedLayoutAsync(HttpClient layouts)
    {
        string name = $"W302-{Guid.NewGuid():N}"[..16];
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, Fab);

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts", new
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

    private async Task<int> CountLayoutsNamedAsync(string name)
    {
        await using LayoutCompositionDbContext context = await aspire.CreateLayoutCompositionDbContextAsync();
        FabIdentifier fab = FabIdentifier.From(Fab);
        LayoutName parsed = LayoutName.From(name);

        return await context.Layouts.CountAsync(candidate => candidate.Fab == fab && candidate.Name == parsed);
    }

    private async Task<int> CountWallsNamedAsync(string name)
    {
        await using LayoutCompositionDbContext context = await aspire.CreateLayoutCompositionDbContextAsync();
        FabIdentifier fab = FabIdentifier.From(Fab);
        WallName parsed = WallName.From(name);

        return await context.Walls.CountAsync(candidate => candidate.Fab == fab && candidate.Name == parsed);
    }

    private async Task<int> CountOverlaysNamedAsync(string name)
    {
        await using OverlayDesignerDbContext context = await aspire.CreateOverlayDesignerDbContextAsync();
        OverlayName parsed = OverlayName.From(name);

        return await context.Overlays.CountAsync(candidate => candidate.Name == parsed);
    }

    private static string UniqueName() => $"t302-{Guid.NewGuid():N}"[..16];

    /// <summary>
    /// <c>VariableName</c> must start with a letter and contain only letters,
    /// digits and underscores — no hyphen, unlike the other contexts' names
    /// (matches <c>VariableFabResolutionIntegrationTests.UniqueName</c>).
    /// </summary>
    private static string UniqueVariableName() => $"t302{Guid.NewGuid():N}"[..16];

    private static async Task<string?> TitleOfAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("title", out JsonElement title)
            ? title.GetString()
            : null;
    }

    private async Task<string> DiagnoseAsync(string resource, HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"{resource} log:{Environment.NewLine}{aspire.RecentLogs(resource)}";
}
