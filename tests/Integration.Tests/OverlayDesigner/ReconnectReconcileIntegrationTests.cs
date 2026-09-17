using System.Diagnostics;
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
/// <see cref="ReconcileCeilingMs"/> of reconnect.
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
    /// <summary>
    /// How long to keep polling before concluding the reconcile is
    /// <b>not coming</b> — governs only the <see cref="CancellationTokenSource"/>
    /// below, and nothing else. Deliberately generous, because it is no longer
    /// the assertion (spec 171 / #2150 split it from <c>ReconcileCeilingMs</c>,
    /// which used to be the same constant playing both roles — so <c>elapsed</c>
    /// could not materially exceed it, and the test could not tell a slow
    /// reconcile from an absent one). This path includes a SignalR re-handshake
    /// against a possibly-cold Aspire stack, and a window that expires during a
    /// slow-but-working reconcile produces the wrong failure — a state failure
    /// naming a defect that is not there.
    /// </summary>
    private static readonly TimeSpan ReconcileWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The bound <c>elapsed</c> is asserted against — independent of
    /// <see cref="ReconcileWindow"/>, with no arithmetic relating the two.
    ///
    /// <para>
    /// <b>The reconcile is synchronous.</b> <c>GetLayoutQueryHandler</c> reads
    /// <c>ILayoutQuerySource</c> — EF over the same Postgres the archive
    /// command committed to before the archive POST returned. There is no
    /// projection and no outbox on this path, so the <b>first</b> GET after
    /// reconnect must already see <c>Archived</c>. What this bounds is one warm
    /// HTTP round trip, not a propagation delay — do not read it as one.
    /// </para>
    ///
    /// <para>
    /// <b>Measured on this machine: 3 clean samples, not the planned 5+.</b>
    /// 33 ms, 25 ms, 104 ms (worst 104 ms). Two other runs from the same
    /// measurement session are <i>not</i> in that count, and are recorded here
    /// rather than discarded silently, per spec 171 (#2150):
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// One run's execution window measurably overlapped a ~30 s stash/pop the
    /// orchestrating session performed on this file to rewrite two earlier
    /// commit messages — a real risk of building against a momentarily
    /// inconsistent working tree, so that run's result is not used regardless
    /// of what it showed.
    /// </description></item>
    /// <item><description>
    /// A separate run, clear of that window, failed outright with
    /// <c>Polly.Timeout.TimeoutRejectedException</c> / a socket abort during
    /// setup — not a measurement, but evidence of the same underlying
    /// constraint: this session's repeated ephemeral Aspire boots, stacked on
    /// this machine's existing load (an IDE and its language-server backend
    /// alone were holding ~4.5 GB), drove free memory down to <b>~3.7 GB of
    /// 23.8 GB</b> and a follow-up measurement batch was killed outright by
    /// the OS ("running low on memory") before it could run at all.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Why 3 samples is enough here, even though the plan asked for 5+.</b>
    /// The derivation below is 6-7x the worst of the 3 (104 ms), landing at
    /// 625-728 ms — roughly 3x of headroom below the 2 s hard stop before the
    /// multiplier's own margin is even counted, and comfortably inside the 4x
    /// clearance below <see cref="ReconcileWindow"/> (30 s / 4 = 7.5 s). A 4th
    /// or 5th sample was not going to move a decision sitting this far from
    /// both boundaries, and the OS had just priced what forcing one more
    /// ephemeral boot costs on this machine. Documenting the constraint
    /// plainly beats manufacturing a cleaner-looking sample count.
    /// </para>
    ///
    /// <para>
    /// <b>700 ms</b>: 6.7x the worst observed (104 ms), the template's own R1
    /// ratio (<c>ResolvedTextReachesItsFabTests.cs:120-132</c> uses ~6.6x)
    /// applied to a figure that, unlike row 1's, was already measured on a
    /// cold Aspire stack over a real HTTP hop rather than a warm dev-box
    /// median — so no extra margin beyond the template's own ratio is added.
    /// R2 holds (700 ms is 42.9x below the 30 s window, well past the
    /// required 4x), and 700 ms is nowhere near the 2 s hard stop, so no
    /// escalation applies.
    /// </para>
    /// </summary>
    private const int ReconcileCeilingMs = 700;

    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reconnected_client_reconciles_an_archived_layout_promptly()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("layout-composition");
        string accessToken = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        // Seed: a Published layout the client is "rendering".
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/layouts",
            new
            {
                name = $"Rcn-{Guid.NewGuid():N}".Substring(0, 16),
                grid = new { rows = 1, cols = 1 },
                tiles = new[] { new { cameraIdentifier = await LayoutRequests.RegisterCameraAsync(aspire), overlayIdentifier = (Guid?)null, row = 0, col = 0 } },
            });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage publish = await LayoutRequests.PostAsync(
            admin, layoutIdentifier, "revisions/1/publish");
        publish.EnsureSuccessStatusCode();

        Uri hubUri = aspire.HubUri("layout-composition", LayoutLifecycleHub.Path);
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
        HttpResponseMessage archived = await LayoutRequests.PostAsync(
            admin, layoutIdentifier, "revisions/1/archive");
        archived.EnsureSuccessStatusCode();

        // Reconnect (manual re-Start since StopAsync forces a fresh handshake).
        await client.StartAsync();

        // Reconcile path: fetch the layout via GET and assert the kiosk would
        // discover the Archived state within the window. The kiosk-web hook
        // does this on `onreconnected`; here we drive the HTTP equivalent
        // directly so the test stays independent of the React surface.
        using CancellationTokenSource window = new(ReconcileWindow);
        Stopwatch stopwatch = Stopwatch.StartNew();
        string observedState = "Unknown";
        while (!window.IsCancellationRequested)
        {
            HttpResponseMessage fetched = await admin.GetAsync($"/layouts/{layoutIdentifier}", window.Token);
            JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>(window.Token);
            JsonElement revision = payload.GetProperty("revisions")[0];
            observedState = revision.GetProperty("state").GetString() ?? "Unknown";
            if (observedState == "Archived")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), window.Token);
        }
        long elapsedMs = stopwatch.ElapsedMilliseconds;

        // The artefact. Recorded unconditionally so the figure survives a green
        // run rather than being computed only to be thrown away (spec 123's
        // finding on six of seven budgets that did exactly that).
        Console.WriteLine(
            $"[spec 171 row 3] reconcile-after-reconnect: {elapsedMs} ms "
            + $"(ceiling {ReconcileCeilingMs} ms, window {ReconcileWindow.TotalSeconds:F0} s)");

        observedState.ShouldBe("Archived");
        elapsedMs.ShouldBeLessThan(
            ReconcileCeilingMs,
            $"reconcile-after-reconnect took {elapsedMs} ms");
    }
}
