using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 063 T002 (#2012) — the seam that had no test: an operator's value
/// change arriving as a <c>ResolvedOverlayTextChanged</c> frame on a real hub
/// connection, in the fab the change happened in and nowhere else.
///
/// <para>
/// <b>Why this level exists.</b> Nine unit tests pass over this path today —
/// six on the producer, three on the consumer — and the defect lives in the
/// gap between them. The producer's tests assert nothing about
/// <c>EventMetadata.Fab</c>; the consumer's tests build their own metadata
/// with a fab already in it. Neither suite can see a producer that publishes
/// no fab into a consumer that drops what carries none. Only a test that
/// crosses the seam can, and until this file there was none.
/// </para>
///
/// <para>
/// <b>What this proves.</b> The whole server-side path: the domain event, the
/// Postgres outbox, RabbitMQ, the LayoutComposition subscriber, the fab guard,
/// the broadcaster, the SignalR group, and a real subscribed client. And that
/// it reaches that fab only.
/// </para>
///
/// <para>
/// <b>What this does not prove.</b> That a React tile re-renders. The client
/// here is a .NET <see cref="HubConnection"/> on a socket, not a browser: no
/// RTK cache, no <c>useLabelDelay</c>, no DOM. A green run of this file with a
/// broken <c>CellPage</c> still means a dark wall. The e2e span check in
/// <c>e2e/kiosk-shows-a-label-over-video.spec.ts</c> is the only level with a
/// tile.
/// </para>
///
/// <para>
/// <b>Which test is red before the fix, and which is not.</b> Only
/// <see cref="A_value_change_reaches_a_hub_client_in_the_fab_it_changed_in"/>
/// is red today. The two negative tests
/// (<see cref="A_munich_value_change_reaches_no_dresden_connection"/> and
/// <see cref="A_refused_value_change_pushes_no_frame_to_anyone"/>) <b>pass
/// today for the wrong reason</b>: nothing is sent to anyone at all, so "not to
/// dresden" and "not to anybody" are trivially true. They are written down
/// anyway, and this paragraph with them, so that a later reader does not
/// mistake their green for coverage that existed before the fix. They become
/// real assertions the moment the producer starts stamping the fab — and they
/// are the assertions that would catch the wrong fix, which is broadcasting to
/// everyone when the fab is missing (ADR-0115, spec 017 FR-015).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ResolvedTextReachesItsFabTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// How long to wait for a frame that should arrive. Generous against the
    /// ~1 s push budget the sibling hub tests use, so a slow stack reads as
    /// slow rather than as a dropped frame.
    /// </summary>
    private static readonly TimeSpan FrameWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long to wait before concluding a frame is not coming. Same constant
    /// and same reasoning as <c>OverlayFrameFabScopingIntegrationTests</c>.
    /// </summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(6);

    /// <summary>
    /// A timeout is a weak failure signal — an unbootable stack and a dropped
    /// frame look identical. This names the log line that tells them apart.
    /// </summary>
    private const string NoFrameDiagnostic =
        "no ResolvedOverlayTextChanged frame reached the munich connection — check the "
        + "LayoutComposition log for `ResolvedOverlayTextChangedWithoutFab`, which means the "
        + "producer published the event with no fab";

    public async Task InitializeAsync()
    {
        await aspire.ResetSystemVariablesAsync();
        await aspire.ResetLayoutCompositionAsync();
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// FR-001, FR-003 and FR-006. The happy path of user story 1: a value set
    /// in munich arrives on a munich screen that was already connected.
    ///
    /// <para>
    /// The figure printed here is constitution §IV's <c>event → overlay
    /// state</c> leg (200 ms), both stamps taken by this process on one clock.
    /// It is <b>server-side only</b> — it excludes the browser, the React
    /// re-render and the label hold.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_value_change_reaches_a_hub_client_in_the_fab_it_changed_in()
    {
        (string variableName, Guid overlay) = await AMunichOverlayBoundToAVariableAsync();

        (HubConnection munich, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> munichFrames) =
            await ListenAsync(await AdminTokenAsync());

        await using (munich)
        {
            Task<ResolvedFrame> arrival = munichFrames.GetOrAdd(overlay, _ => new()).Task;

            using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
            (await VariableRequests.SetValueAsync(variables, variableName, "82.5")).EnsureSuccessStatusCode();

            // The clock starts when the write returns, so the figure is the
            // push leg and not the write.
            Stopwatch stopwatch = Stopwatch.StartNew();
            await Task.WhenAny(arrival, Task.Delay(FrameWindow));
            stopwatch.Stop();

            arrival.IsCompletedSuccessfully.ShouldBeTrue(NoFrameDiagnostic);

            ResolvedFrame frame = await arrival;
            frame.ResolvedText.ShouldBe("Line 1: 82.5");

            // The artefact. The assertion below is an order-of-magnitude
            // regression guard, not the budget: this instrument includes a cold
            // JIT and container scheduling on shared CI, so a bound at 200 ms
            // would police the budget with a ruler that cannot read it.
            Console.WriteLine(
                $"[#2012 §IV event -> overlay state] value write returned -> ResolvedOverlayTextChanged "
                + $"frame on a subscribed munich hub client: {stopwatch.ElapsedMilliseconds} ms "
                + $"(budget 200 ms; server-side only — excludes the browser, the React re-render "
                + $"and the ADR-0129 label hold)");

            stopwatch.ElapsedMilliseconds.ShouldBeLessThan((long)FrameWindow.TotalMilliseconds);
        }
    }

    /// <summary>
    /// FR-003, and the failure direction that actually matters. "Doesn't
    /// update" is an annoyance; "updates on the wrong wall" puts one plant's
    /// production figure on another plant's screens.
    ///
    /// <para>
    /// Green today for the wrong reason — see the class remarks. The fix this
    /// spec forbids (broadcast to all when the fab is missing) is the one that
    /// would turn this red.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_munich_value_change_reaches_no_dresden_connection()
    {
        (string variableName, Guid overlay) = await AMunichOverlayBoundToAVariableAsync();

        (HubConnection dresden, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> dresdenFrames) =
            await ListenAsync(await aspire.GetAccessTokenAsync(DresdenOperator, OperatorPassword));

        await using (dresden)
        {
            using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
            (await VariableRequests.SetValueAsync(variables, variableName, "82.5")).EnsureSuccessStatusCode();

            await Task.Delay(SilenceWindow);

            dresdenFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse(
                "a dresden screen was told a munich variable's value");
        }
    }

    /// <summary>
    /// The bad-request shape. A value the variable's declared type refuses is
    /// answered 400, and nothing is pushed to anyone — a rejected write must
    /// not move a label on any wall.
    ///
    /// <para>
    /// Green today for the wrong reason, exactly as the sibling negative is.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_refused_value_change_pushes_no_frame_to_anyone()
    {
        (string variableName, Guid overlay) = await AMunichOverlayBoundToAVariableAsync();

        (HubConnection munich, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> munichFrames) =
            await ListenAsync(await AdminTokenAsync());
        (HubConnection dresden, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> dresdenFrames) =
            await ListenAsync(await aspire.GetAccessTokenAsync(DresdenOperator, OperatorPassword));

        await using (munich)
        await using (dresden)
        {
            using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

            HttpResponseMessage refused = await VariableRequests.SetValueAsync(
                variables, variableName, "not-a-number");
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            await Task.Delay(SilenceWindow);

            munichFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse(
                "a refused write pushed a frame to munich");
            dresdenFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse(
                "a refused write pushed a frame to dresden");
        }
    }

    // ---- helpers ------------------------------------------------------------

    private sealed record ResolvedFrame(Guid Overlay, string ResolvedText, long Version);

    private async Task<(HubConnection Connection, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> Frames)>
        ListenAsync(string accessToken)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(aspire.HubUri("layout-composition", LayoutLifecycleHub.Path), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

        ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> frames = new();
        connection.On<JsonElement>(
            nameof(ILayoutLifecycleClient.ResolvedOverlayTextChanged),
            payload =>
            {
                ResolvedFrame frame = new(
                    payload.GetProperty("overlay").GetGuid(),
                    payload.GetProperty("resolvedText").GetString() ?? string.Empty,
                    payload.GetProperty("version").GetInt64());
                frames.GetOrAdd(frame.Overlay, _ => new()).TrySetResult(frame);
            });

        await connection.StartAsync();

        return (connection, frames);
    }

    private Task<string> AdminTokenAsync() =>
        aspire.GetAccessTokenAsync(AspireFixture.AdminUsername, AspireFixture.AdminPassword);

    /// <summary>
    /// The arrangement every test here shares: a munich variable, a published
    /// overlay whose label embeds its placeholder, and a published munich
    /// layout referencing that overlay — the state a fab wall is permanently
    /// in. Returns once the overlay actually resolves, because the reverse
    /// index is populated by an integration event and publish returning is not
    /// enough (the reasoning is <c>NFR_VariableResolutionLatencyTests</c>').
    /// </summary>
    private async Task<(string VariableName, Guid Overlay)> AMunichOverlayBoundToAVariableAsync()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        string variableName = $"v{Guid.NewGuid():N}"[..12];
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name = variableName,
            type = "Number",
            initialValue = "0",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();

        HttpResponseMessage created = await overlays.PostAsJsonAsync("/overlays", new
        {
            name = $"Res-{Guid.NewGuid():N}"[..16],
            label = new
            {
                text = $"Line 1: {{{{{variableName}}}}}",
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

        await PublishAMunichLayoutReferencingAsync(overlay);
        await WaitUntilResolvableAsync(variables, overlay, variableName);

        return (variableName, overlay);
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

    /// <summary>
    /// Until the reverse index knows the overlay, the snapshot renders the
    /// literal placeholder. Its disappearance is the readiness signal — and
    /// asserting it over HTTP before touching the hub means a broken fixture
    /// fails here, differently, rather than as a mystery timeout later.
    /// </summary>
    private static async Task WaitUntilResolvableAsync(
        HttpClient variables, Guid overlay, string variableName)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 30_000)
        {
            HttpResponseMessage snapshot = await variables.GetAsync(
                $"/system-variables/snapshot?overlayIdentifier={overlay}");
            if (snapshot.IsSuccessStatusCode)
            {
                JsonElement payload = await snapshot.Content.ReadFromJsonAsync<JsonElement>();
                string resolved = payload.GetProperty("resolvedText").GetString() ?? string.Empty;
                if (!resolved.Contains($"{{{{{variableName}}}}}", StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        throw new TimeoutException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Overlay {overlay} never became resolvable; the reverse index did not pick it up."));
    }
}
