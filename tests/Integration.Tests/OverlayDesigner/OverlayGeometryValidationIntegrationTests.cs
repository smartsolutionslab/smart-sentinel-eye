using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 060 US1 scenario 3 — the <c>400</c> an out-of-range label geometry
/// earns from both write endpoints.
///
/// <para>
/// Both <c>Label.From</c> call sites sit inside a
/// <c>try { … } catch (ArgumentException ex) { 400 }</c>, and the caught
/// exception's message is copied verbatim into the problem's <c>detail</c>.
/// Nothing asserted either fact, so building the geometry one statement above
/// that <c>try</c> would turn every out-of-range coordinate into a <c>500</c>
/// with the whole suite still green.
/// </para>
///
/// <para>
/// The out-of-range values are whole numbers on purpose. <c>decimal.ToString()</c>
/// follows the API process's culture, so <c>1.01m</c> reaches the caller as
/// <c>1,01</c> on a comma-decimal host and <c>1.01</c> on an invariant one —
/// an exact-text assertion on a fractional value passes in CI and fails on a
/// developer machine, or the reverse. <c>2</c> and <c>-1</c> format identically
/// everywhere and are just as far outside <c>[0, 1]</c>.
/// </para>
///
/// <para>
/// The coordinate cases assert the <c>detail</c> exactly, because the
/// <c>[0, 1]</c> wording is contract-visible and must survive unchanged. The
/// extent cases assert the parameter name and the <c>(0, 1]</c> interval rather
/// than the whole sentence: spec 060 research R2 declares the extent message's
/// remaining wording as the one accepted difference, so pinning it
/// character-for-character here would pin a string the spec says may move.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayGeometryValidationIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object LabelBody(
        decimal normalizedX = 0.5m,
        decimal normalizedY = 0.05m,
        decimal normalizedWidth = 0.3m,
        decimal normalizedHeight = 0.08m) => new
        {
            text = "Production Line 1",
            normalizedX,
            normalizedY,
            normalizedWidth,
            normalizedHeight,
            fontSizePx = 48,
        };

    [Fact]
    public async Task Create_with_a_coordinate_outside_the_unit_range_returns_400_OVERLAY_INVALID_INPUT()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage response = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Geo-{Guid.NewGuid():N}"[..16],
                label = LabelBody(normalizedY: 2m),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_INVALID_INPUT");
        problem.GetProperty("detail").GetString()
            .ShouldBe("normalizedY must be in [0, 1]; got 2. (Parameter 'normalizedY')");
    }

    [Fact]
    public async Task Create_with_an_extent_outside_the_unit_range_returns_400_OVERLAY_INVALID_INPUT()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage response = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Geo-{Guid.NewGuid():N}"[..16],
                label = LabelBody(normalizedWidth: 0m),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_INVALID_INPUT");
        string detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain("normalizedWidth");
        detail.ShouldContain("(0, 1]");
    }

    [Fact]
    public async Task Edit_with_a_coordinate_outside_the_unit_range_returns_400_OVERLAY_INVALID_INPUT()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        Guid overlayIdentifier = await CreateDraftAsync(overlays);

        HttpResponseMessage response = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, "revisions/1",
            new { label = LabelBody(normalizedX: -1m) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_INVALID_INPUT");
        problem.GetProperty("detail").GetString()
            .ShouldBe("normalizedX must be in [0, 1]; got -1. (Parameter 'normalizedX')");
    }

    [Fact]
    public async Task Edit_with_an_extent_outside_the_unit_range_returns_400_OVERLAY_INVALID_INPUT()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        Guid overlayIdentifier = await CreateDraftAsync(overlays);

        HttpResponseMessage response = await OverlayRequests.PatchAsync(
            overlays, overlayIdentifier, "revisions/1",
            new { label = LabelBody(normalizedHeight: 2m) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_INVALID_INPUT");
        string detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain("normalizedHeight");
        detail.ShouldContain("(0, 1]");
    }

    /// <summary>
    /// The bounds themselves are accepted. Without this the four tests above are
    /// satisfied by an endpoint that refuses every geometry it is given.
    /// </summary>
    [Fact]
    public async Task A_label_on_the_unit_bounds_is_accepted()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage response = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Geo-{Guid.NewGuid():N}"[..16],
                label = LabelBody(normalizedX: 0m, normalizedY: 1m, normalizedWidth: 1m, normalizedHeight: 1m),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient overlays)
    {
        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Geo-{Guid.NewGuid():N}"[..16],
                label = LabelBody(),
            });
        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
