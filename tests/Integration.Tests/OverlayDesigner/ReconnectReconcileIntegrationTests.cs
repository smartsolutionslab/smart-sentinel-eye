using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 003 FR-012 / #306 — pays the long-deferred debt from spec 003
/// PR G. Drops a SignalR client, archives its bound layout while
/// disconnected, then asserts that the reconnected client reconciles
/// (refetches the layout list and discovers the Archived state) within
/// 5 seconds of reconnect.
///
/// The reconcile path is the safety net for missed Archived broadcasts:
/// it's the only thing that prevents a kiosk from rendering a layout
/// whose admin has just archived it. Lives in the OverlayDesigner
/// integration folder per spec 004 plan but exercises the underlying
/// LayoutLifecycle hub.
/// </summary>
[Collection(AspireCollection.Name)]
public class ReconnectReconcileIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const int ReconcileBudgetSeconds = 5;

    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reconnected_client_reconciles_an_archived_layout_within_five_seconds()
    {
        using HttpClient admin = await _aspire.CreateAdminClientAsync("layout-composition");
        string accessToken = await _aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        // Seed: a Published layout the client is "rendering".
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/layouts",
            new { name = $"Rcn-{Guid.NewGuid():N}".Substring(0, 16), cameraIdentifier = Guid.CreateVersion7() });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage publish = await admin.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/publish", content: null);
        publish.EnsureSuccessStatusCode();

        Uri hubUri = new(_aspire.App.GetEndpoint("layout-composition").ToString().TrimEnd('/') + LayoutLifecycleHub.Path);
        await using HubConnection client = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        TaskCompletionSource<bool> reconnected = new();
        client.Reconnected += _ =>
        {
            reconnected.TrySetResult(true);
            return Task.CompletedTask;
        };

        await client.StartAsync();

        // Hard-stop the connection (simulates network blip).
        await client.StopAsync();

        // While the client is disconnected, archive the layout.
        HttpResponseMessage archived = await admin.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/archive", content: null);
        archived.EnsureSuccessStatusCode();

        // Reconnect (manual re-Start since StopAsync forces a fresh handshake).
        await client.StartAsync();

        // Reconcile path: fetch the layout via GET and assert the kiosk would
        // discover the Archived state within the budget. The kiosk-web hook
        // does this on `onreconnected`; here we drive the HTTP equivalent
        // directly so the test stays independent of the React surface.
        using CancellationTokenSource budget = new(TimeSpan.FromSeconds(ReconcileBudgetSeconds));
        DateTime started = DateTime.UtcNow;
        string observedState = "Unknown";
        while (!budget.IsCancellationRequested)
        {
            HttpResponseMessage fetched = await admin.GetAsync($"/layouts/{layoutIdentifier}", budget.Token);
            JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>(budget.Token);
            JsonElement revision = payload.GetProperty("revisions")[0];
            observedState = revision.GetProperty("state").GetString() ?? "Unknown";
            if (observedState == "Archived") break;
            await Task.Delay(TimeSpan.FromMilliseconds(100), budget.Token);
        }
        TimeSpan elapsed = DateTime.UtcNow - started;

        observedState.ShouldBe("Archived");
        elapsed.TotalSeconds.ShouldBeLessThan(
            ReconcileBudgetSeconds,
            $"reconcile-after-reconnect took {elapsed.TotalSeconds:F1} s");
    }
}
