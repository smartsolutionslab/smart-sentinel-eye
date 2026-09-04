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
/// <b>Which tests were red before the fix.</b> Every test here that awaits a
/// frame, because before the fix no frame was sent at all:
/// <see cref="A_value_change_reaches_a_hub_client_in_the_fab_it_changed_in"/>,
/// <see cref="Successive_value_changes_carry_strictly_increasing_versions"/>,
/// and the positive half of
/// <see cref="A_munich_value_change_reaches_munich_and_not_dresden"/>.
/// </para>
///
/// <para>
/// <b>Why the fab assertion is not its own test.</b> On a trigger of its own,
/// "no dresden frame" is trivially true whenever nothing is sent to anyone —
/// it would pass against the defect, and against a broadcaster that had
/// stopped working entirely. Holding both connections over <b>one</b> write
/// makes the munich arrival the positive control for the dresden silence, so
/// the two cannot disagree about timing. That is the shape
/// <c>OverlayFrameFabScopingIntegrationTests</c> uses, and citing that file as
/// precedent while splitting the directions across two triggers was the defect
/// this file carried.
/// </para>
///
/// <para>
/// <see cref="A_refused_value_change_pushes_no_frame_to_anyone"/> keeps that
/// weakness by necessity: a refused write produces no frame anywhere, so there
/// is nothing to control against and its 400 is the only positive signal
/// available. It was green before the fix for the wrong reason — nothing was
/// sent to anyone, so "not to anybody" was free — and is written down anyway,
/// so a later reader does not mistake its green for coverage that predates the
/// fix. It, with the scoping test above, is what would catch the wrong fix:
/// broadcasting to everyone when the fab is missing (ADR-0115, spec 017
/// FR-015).
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
    /// The ceiling the push leg is asserted against — <b>not</b> the 200 ms
    /// budget, which this instrument cannot read (cold JIT, container
    /// scheduling, shared CI). Phase 5 measured this exact server-side leg at
    /// <b>555 ms and 758 ms on a cold stack</b>, so 5 s sits roughly 6.6x above
    /// the worst figure anyone has observed and four times below
    /// <see cref="FrameWindow"/>. Both margins are deliberate: a bound at
    /// <see cref="FrameWindow"/> would assert nothing at all, because the wait
    /// has already ended by then, and a bound near the observed figures
    /// (800 ms, as the sibling NFR test uses for a different instrument) would
    /// flake on a cold stack and be deleted by the next person.
    /// </summary>
    private const long PushCeilingMs = 5_000;

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
            // push leg and not the write. The window is cancelled as soon as
            // the race is decided, so a won race does not leave a 20 s timer
            // ticking behind every run of this file.
            using CancellationTokenSource window = new();
            Stopwatch stopwatch = Stopwatch.StartNew();
            await Task.WhenAny(arrival, Task.Delay(FrameWindow, window.Token));
            stopwatch.Stop();
            await window.CancelAsync();

            arrival.IsCompletedSuccessfully.ShouldBeTrue(NoFrameDiagnostic);

            ResolvedFrame frame = await arrival;
            frame.ResolvedText.ShouldBe("Line 1: 82.5");

            // A frame the kiosk would discard is not a delivered frame: the
            // drop guard in `CellPage.tsx` compares against `?? 0`, so a
            // version 0 never reaches a tile. Monotonicity across writes is
            // asserted by Successive_value_changes_carry_strictly_increasing_versions.
            frame.Version.ShouldBeGreaterThan(0);

            // The artefact. See PushCeilingMs for why the assertion below is a
            // regression ceiling rather than the budget.
            Console.WriteLine(
                $"[#2012 §IV event -> overlay state] value write returned -> ResolvedOverlayTextChanged "
                + $"frame on a subscribed munich hub client: {stopwatch.ElapsedMilliseconds} ms "
                + $"(budget 200 ms; asserted ceiling {PushCeilingMs} ms; server-side only — excludes "
                + $"the browser, the React re-render and the ADR-0129 label hold)");

            stopwatch.ElapsedMilliseconds.ShouldBeLessThan(PushCeilingMs);
        }
    }

    /// <summary>
    /// FR-003, and the failure direction that actually matters. "Doesn't
    /// update" is an annoyance; "updates on the wrong wall" puts one plant's
    /// production figure on another plant's screens.
    ///
    /// <para>
    /// Both directions on <b>one</b> write, holding both connections, so the
    /// munich arrival is the positive control for the dresden silence and the
    /// two cannot disagree about timing. See the class remarks for why the
    /// dresden half asserts nothing on its own.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_munich_value_change_reaches_munich_and_not_dresden()
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
            (await VariableRequests.SetValueAsync(variables, variableName, "82.5")).EnsureSuccessStatusCode();

            // Munich's published layout shows the overlay, so munich is told.
            // Awaiting it here is what makes the line below an assertion: with
            // no frame proven to have been sent, "not to dresden" is free.
            ResolvedFrame frame = await munichFrames.GetOrAdd(overlay, _ => new()).Task
                .WaitAsync(FrameWindow);
            frame.ResolvedText.ShouldBe("Line 1: 82.5");

            dresdenFrames.GetOrAdd(overlay, _ => new()).Task.IsCompleted.ShouldBeFalse(
                "a dresden screen was told a munich variable's value");
        }
    }

    /// <summary>
    /// FR-006, and the property the kiosk silently depends on. The tile's drop
    /// guard (<c>onResolvedOverlayTextChanged</c> in <c>CellPage.tsx</c>)
    /// discards any frame whose version is not strictly higher than the last it
    /// applied. A producer that repeated or lowered a version would leave every
    /// wall frozen on its first value while every server-side assertion above
    /// stayed green — the frames arrive, they are simply thrown away on receipt.
    /// </summary>
    [Fact]
    public async Task Successive_value_changes_carry_strictly_increasing_versions()
    {
        (string variableName, Guid overlay) = await AMunichOverlayBoundToAVariableAsync();

        (HubConnection munich, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> munichFrames) =
            await ListenAsync(await AdminTokenAsync());

        await using (munich)
        {
            using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

            ResolvedFrame first = await WriteAndAwaitFrameAsync(
                variables, munichFrames, variableName, overlay, "82.5");
            ResolvedFrame second = await WriteAndAwaitFrameAsync(
                variables, munichFrames, variableName, overlay, "91.5");

            first.ResolvedText.ShouldBe("Line 1: 82.5");
            second.ResolvedText.ShouldBe("Line 1: 91.5");
            second.Version.ShouldBeGreaterThan(first.Version);
        }
    }

    /// <summary>
    /// The bad-request shape. A value the variable's declared type refuses is
    /// answered 400, and nothing is pushed to anyone — a rejected write must
    /// not move a label on any wall.
    ///
    /// <para>
    /// The one test here with no positive control, because a refused write
    /// produces no frame to control against — see the class remarks.
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

    /// <summary>
    /// Writes a value and returns the frame it produced. The completion source
    /// is replaced before the write because it fires once: without the swap the
    /// second write would be answered by the first write's frame, and a version
    /// comparison against a repeated frame proves nothing.
    /// </summary>
    private static async Task<ResolvedFrame> WriteAndAwaitFrameAsync(
        HttpClient variables,
        ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> frames,
        string variableName,
        Guid overlay,
        string value)
    {
        TaskCompletionSource<ResolvedFrame> next = new();
        frames[overlay] = next;

        (await VariableRequests.SetValueAsync(variables, variableName, value)).EnsureSuccessStatusCode();

        return await next.Task.WaitAsync(FrameWindow);
    }

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
