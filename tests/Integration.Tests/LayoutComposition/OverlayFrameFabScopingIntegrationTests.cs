using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 017 T031 — SC-003 and SC-004 with real hub connections, one per fab.
///
/// <para>
/// Every assertion here that matters is an assertion about a frame that must
/// <b>not</b> arrive, over a bounded wait. That is the whole difficulty of
/// testing Half B: FR-011 and FR-013 are invisible when they work, because
/// nothing happens either way. A test that only checked "the right screen got
/// it" would pass against a broadcaster that still sent to everyone.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayFrameFabScopingIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// How long to wait before concluding a frame is not coming. Generous
    /// relative to the ~1 s push SLO the sibling test measures, so a slow
    /// stack reads as slow rather than as correct scoping.
    /// </summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(6);

    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// SC-004, both directions at once: the fab that uses the overlay is told,
    /// and the fab that does not is not. Asserted on one publish so the two
    /// cannot disagree about timing.
    /// </summary>
    [Fact]
    public async Task An_overlay_used_only_in_dresden_reaches_dresden_and_not_munich()
    {
        Guid overlay = await CreatePublishedOverlayAsync();
        await ReferenceFromAPublishedLayoutAsync(overlay, DresdenOperator, "dresden");

        (HubConnection dresden, ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> dresdenFrames) =
            await ListenAsync(await TokenForAsync(DresdenOperator));
        (HubConnection munich, ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> munichFrames) =
            await ListenAsync(await AdminTokenAsync());

        await using (dresden)
        await using (munich)
        {
            await PublishNextRevisionAsync(overlay);

            // Dresden displays it, so dresden is told.
            await dresdenFrames.GetOrAdd(overlay, _ => new()).Task.WaitAsync(SilenceWindow);

            // Munich does not, so munich hears nothing. This is the assertion
            // the feature exists for: the frame carries the overlay's text.
            munichFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse(
                "a munich screen was told about an overlay only dresden uses");
        }
    }

    /// <summary>
    /// FR-011. An overlay no published layout references reaches nobody —
    /// invisible when it works, which is exactly why it is written down.
    /// </summary>
    [Fact]
    public async Task An_overlay_no_layout_references_reaches_nobody()
    {
        Guid overlay = await CreatePublishedOverlayAsync();

        (HubConnection dresden, ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> dresdenFrames) =
            await ListenAsync(await TokenForAsync(DresdenOperator));
        (HubConnection munich, ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> munichFrames) =
            await ListenAsync(await AdminTokenAsync());

        await using (dresden)
        await using (munich)
        {
            await PublishNextRevisionAsync(overlay);
            await Task.Delay(SilenceWindow);

            dresdenFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse();
            munichFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse();
        }
    }

    /// <summary>
    /// FR-013. A draft's tiles do not count as a reference, so the fab whose
    /// only use is unpublished is not told either.
    /// </summary>
    [Fact]
    public async Task An_overlay_referenced_only_by_a_draft_reaches_nobody()
    {
        Guid overlay = await CreatePublishedOverlayAsync();
        await ReferenceFromADraftLayoutAsync(overlay, DresdenOperator);

        (HubConnection dresden, ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> dresdenFrames) =
            await ListenAsync(await TokenForAsync(DresdenOperator));

        await using (dresden)
        {
            await PublishNextRevisionAsync(overlay);
            await Task.Delay(SilenceWindow);

            dresdenFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse();
        }
    }

    // ---- helpers ------------------------------------------------------------

    private async Task<(HubConnection Connection, ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> Frames)>
        ListenAsync(string accessToken)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(aspire.HubUri("layout-composition", LayoutLifecycleHub.Path), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

        ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> frames = new();
        connection.On<JsonElement>(
            nameof(ILayoutLifecycleClient.OverlayRevisionPublished),
            payload =>
            {
                Guid overlay = payload.GetProperty("overlay").GetGuid();
                frames.GetOrAdd(overlay, _ => new()).TrySetResult(overlay);
            });

        await connection.StartAsync();
        return (connection, frames);
    }

    private Task<string> TokenForAsync(string username) =>
        aspire.GetAccessTokenAsync(username, OperatorPassword);

    private Task<string> AdminTokenAsync() =>
        aspire.GetAccessTokenAsync(AspireFixture.AdminUsername, AspireFixture.AdminPassword);

    private async Task<Guid> CreatePublishedOverlayAsync()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync("/overlays", new
        {
            name = $"Ovl-{Guid.NewGuid():N}"[..16],
            label = new
            {
                text = "Production Line 1",
                normalizedX = 0.5m,
                normalizedY = 0.05m,
                normalizedWidth = 0.3m,
                normalizedHeight = 0.08m,
                fontSizePx = 48,
            },
        });
        created.EnsureSuccessStatusCode();

        Guid overlay = await created.Content.ReadFromJsonAsync<Guid>();
        (await OverlayRequests.PostAsync(overlays, overlay, "revisions/1/publish")).EnsureSuccessStatusCode();
        return overlay;
    }

    /// <summary>Publishes a fresh revision, which is what emits the frame.</summary>
    private async Task PublishNextRevisionAsync(Guid overlay)
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        (await OverlayRequests.PostAsync(overlays, overlay, "draft")).EnsureSuccessStatusCode();
        (await OverlayRequests.PostAsync(overlays, overlay, "revisions/2/publish")).EnsureSuccessStatusCode();
    }

    private async Task ReferenceFromAPublishedLayoutAsync(Guid overlay, string username, string fab)
    {
        (HttpClient layouts, Guid layout) = await CreateReferencingDraftAsync(overlay, username, fab);
        using (layouts)
        {
            (await LayoutRequests.PostAsync(layouts, layout, "revisions/1/publish")).EnsureSuccessStatusCode();
        }
    }

    private async Task ReferenceFromADraftLayoutAsync(Guid overlay, string username)
    {
        (HttpClient layouts, _) = await CreateReferencingDraftAsync(overlay, username, "dresden");
        layouts.Dispose();
    }

    private async Task<(HttpClient Layouts, Guid Layout)> CreateReferencingDraftAsync(
        Guid overlay, string username, string fab)
    {
        HttpClient layouts = await aspire.CreateAuthenticatedClientAsync(
            "layout-composition", username, OperatorPassword);

        HttpResponseMessage created = await layouts.PostAsJsonAsync("/layouts", new
        {
            name = $"Ref-{Guid.NewGuid():N}"[..16],
            grid = new { rows = 1, cols = 1 },
            tiles = new[]
            {
                new
                {
                    // FR-014: the camera must be in the layout's own fab.
                    cameraIdentifier = await LayoutRequests.RegisterCameraAsync(aspire, fab),
                    overlayIdentifier = (Guid?)overlay,
                    row = 0,
                    col = 0,
                },
            },
        });
        created.EnsureSuccessStatusCode();

        return (layouts, await created.Content.ReadFromJsonAsync<Guid>());
    }
}
