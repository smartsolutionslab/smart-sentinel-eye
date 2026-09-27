using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 274, plan.md §3.3 (issue #2358) — after the literal reads move off
/// the <c>{name}</c> collision path, <c>GetOverlaySnapshot</c> and
/// <c>ResolveOverlayText</c> must keep answering exactly as before, just at
/// <c>/system-variables/-/snapshot</c> and <c>/system-variables/-/resolve</c>.
///
/// <para>
/// <b>Red today, and for a different reason than <see cref="ShadowedVariableNameIntegrationTests"/>.</b>
/// <c>SystemVariableEndpoints.cs</c> does not map either <c>/-/</c> path yet,
/// so both facts here 404 — no endpoint at all, not the wrong one. Each
/// exercises the real handler end-to-end (a resolved overlay snapshot, a
/// resolved preview), not merely that the route exists, so a fix that maps
/// the new path but breaks what it returns is still caught.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MovedSnapshotAndResolveRoutesIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetSystemVariablesAsync();
        await aspire.ResetLayoutCompositionAsync();
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_overlay_snapshot_still_resolves_at_its_new_route()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        string variableName = VariableRequests.UniqueName();
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name = variableName,
            type = "Number",
            initialValue = "42",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();

        Guid overlay = await OverlayRequests.PublishWithLabelAsync(
            overlays, $"Value: {{{{{variableName}}}}}", "Rst");

        await PublishAMunichLayoutReferencingAsync(overlay);
        // Waits via the OLD /snapshot path (unmoved fixture, out of scope for
        // this change) purely as a readiness signal that the reverse index
        // has picked the overlay up; the assertion below reads the NEW path.
        await OverlaySnapshotReadiness.WaitUntilResolvableAsync(variables, overlay, variableName);

        HttpResponseMessage response = await variables.GetAsync(
            $"/system-variables/-/snapshot?overlayIdentifier={overlay}&fabId=munich");
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "GET /system-variables/-/snapshot did not answer 200 — the moved route does not exist yet "
            + $"(SystemVariableEndpoints.cs still maps only /snapshot, issue #2358). Response: {body}");

        OverlaySnapshotReadiness.ResolvedTextIn(body).ShouldBe("Value: 42");
    }

    [Fact]
    public async Task The_resolve_preview_still_resolves_at_its_new_route()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = VariableRequests.UniqueName();
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = "7",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();

        HttpResponseMessage response = await variables.GetAsync(
            $"/system-variables/-/resolve?text={Uri.EscapeDataString($"{{{{{name}}}}}")}&fabId=munich");
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "GET /system-variables/-/resolve did not answer 200 — the moved route does not exist yet "
            + $"(SystemVariableEndpoints.cs still maps only /resolve, issue #2358). Response: {body}");

        JsonDocument.Parse(body).RootElement.GetProperty("resolvedText").GetString().ShouldBe("7");
    }

    private async Task PublishAMunichLayoutReferencingAsync(Guid overlay)
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts", new
        {
            name = $"Wall-{Guid.NewGuid():N}"[..16],
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new
                {
                    cameraIdentifier = await LayoutRequests.RegisterCameraAsync(aspire, "munich"),
                    overlayIdentifier = (Guid?)overlay,
                    row = 0,
                    col = 0,
                },
            },
        });
        created.EnsureSuccessStatusCode();

        Guid layout = await created.Content.ReadFromJsonAsync<Guid>();
        (await LayoutRequests.PostAsync(layouts, layout, "revisions/1/publish")).EnsureSuccessStatusCode();
    }
}
