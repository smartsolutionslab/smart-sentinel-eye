using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 202 T001/T002 (#2426) — the restart bug itself. The wire contract
/// documents <c>ResolvedOverlayTextChangedV1.Version</c> as "a monotonic
/// per-overlay counter the kiosk uses to discard out-of-order frames"
/// (<c>ResolvedOverlayTextChangedV1.cs:20</c>), but the counter lives in a
/// <c>ConcurrentDictionary</c> on <c>InMemoryReverseIndex</c> and resets to 1
/// on every process start. A kiosk holding a higher mark from before an
/// ordinary redeploy then drops every push as stale, indefinitely.
///
/// <para>
/// <b>This is the true behavioural red</b> the phase 4a gate requires
/// (constitution §Testing, ADR-0139): both tests here compile against the
/// existing public surface — real HTTP, a real SignalR hub, a real resource
/// restart — and are expected to fail at an assertion today, not at
/// compilation. Contrast <c>OverlayTextVersionStoreIntegrationTests</c> and
/// the Application-layer fan-out tests, which are red by <i>compilation</i>
/// because they introduce <c>IOverlayTextVersions</c> — a weaker form of
/// evidence, per tasks.md.
/// </para>
///
/// <para>
/// <b>Two independent restarts, not one shared one.</b> xUnit constructs a
/// fresh instance of this class per <c>[Fact]</c>, so sharing one restart
/// across <see cref="The_push_path_survives_a_restart"/> (T001, SC-1) and
/// <see cref="The_snapshot_path_survives_a_restart"/> (T002, SC-2) would need
/// a class-fixture-of-a-class-fixture arrangement that buys nothing for two
/// tests: each restart is a few minutes of real Aspire/Postgres time either
/// way, and two independently-attributable failures are worth more than one
/// restart saved. Both are excluded from CI by category, same as
/// <c>RestartLosesNothingIntegrationTests</c> — "Failed to stop resource" on
/// the runner is a platform limitation, not a code defect, and this is
/// verified by hand instead (spec.md's independent end-to-end procedure,
/// phase 5).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
[Trait("Category", "Disruptive")]
public class VersionSurvivesARestartTests(AspireFixture aspire, ITestOutputHelper output) : IAsyncLifetime
{
    private const string ResourceName = "system-variables";

    /// <summary>Same reasoning and same figure as <c>ResolvedTextReachesItsFabTests</c>.</summary>
    private static readonly TimeSpan FrameWindow = TimeSpan.FromSeconds(20);

    public async Task InitializeAsync()
    {
        await aspire.ResetSystemVariablesAsync();
        await aspire.ResetLayoutCompositionAsync();
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// SC-1 — the push path. Three changes before a restart establish the
    /// version the kiosk would be holding; a fourth after the restart must
    /// carry a strictly higher version, or a real kiosk drops it as stale and
    /// the wall freezes on the third value forever.
    /// </summary>
    [Fact]
    public async Task The_push_path_survives_a_restart()
    {
        (string variableName, Guid overlay) = await AMunichOverlayBoundToAVariableAsync();

        (HubConnection hub, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> frames) =
            await ListenAsync();

        await using (hub)
        {
            using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

            long beforeRestart1 = (await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "1")).Version;
            long beforeRestart2 = (await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "2")).Version;
            long beforeRestart3 = (await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "3")).Version;
            output.WriteLine($"versions before restart: {beforeRestart1}, {beforeRestart2}, {beforeRestart3}");

            await RestartAsync(ResourceName);
            output.WriteLine($"restarted {ResourceName}");

            long afterRestart = (await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "4")).Version;
            output.WriteLine($"version after restart: {afterRestart}");

            // #2426 — a process-lifetime counter restarts at 1 here, which is
            // not greater than beforeRestart3. A real kiosk's drop filter
            // (`message.version <= (versions.get(...) ?? 0)` in CellPage.tsx)
            // then discards this frame and every subsequent one until the new
            // counter climbs back past what the kiosk already holds.
            afterRestart.ShouldBeGreaterThan(
                beforeRestart3,
                $"the version after restarting {ResourceName} ({afterRestart}) was not strictly greater than "
                + $"the highest version issued before the restart ({beforeRestart3}) — a connected kiosk holding "
                + $"{beforeRestart3} as its high-water mark would silently discard this push and every "
                + "push after it, as stale, until the counter climbed back past its own history (#2426)");
        }
    }

    /// <summary>
    /// SC-2 — the REST snapshot path. No client reads this today (spec.md
    /// Finding A), but the wire contract documents it and the first consumer
    /// to trust it inherits the same defect. No post-restart write happens
    /// here: the question is whether the snapshot alone survived, not
    /// whether a subsequent push repairs it.
    /// </summary>
    [Fact]
    public async Task The_snapshot_path_survives_a_restart()
    {
        (string variableName, Guid overlay) = await AMunichOverlayBoundToAVariableAsync();

        (HubConnection hub, ConcurrentDictionary<Guid, TaskCompletionSource<ResolvedFrame>> frames) =
            await ListenAsync();

        long highestBeforeRestart;
        await using (hub)
        {
            using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

            await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "1");
            await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "2");
            highestBeforeRestart = (await WriteAndAwaitFrameAsync(variables, frames, variableName, overlay, "3")).Version;
        }

        output.WriteLine($"highest version before restart: {highestBeforeRestart}");

        await RestartAsync(ResourceName);
        output.WriteLine($"restarted {ResourceName}");

        using HttpClient readers = await aspire.CreateAdminClientAsync("system-variables");
        HttpResponseMessage snapshot = await readers.GetAsync(
            $"/system-variables/snapshot?overlayIdentifier={overlay}&fabId=munich");
        snapshot.EnsureSuccessStatusCode();

        JsonElement payload = await snapshot.Content.ReadFromJsonAsync<JsonElement>();
        long versionAfterRestart = payload.GetProperty("version").GetInt64();
        output.WriteLine($"snapshot version after restart: {versionAfterRestart}");

        // #2426 Finding A — CurrentVersionFor resets to 0 on the same
        // restart. Today's answer is 0, which is lower than every version
        // issued before the restart.
        versionAfterRestart.ShouldBeGreaterThanOrEqualTo(
            highestBeforeRestart,
            $"the snapshot's version after restarting {ResourceName} ({versionAfterRestart}) was lower than "
            + $"the highest version issued before the restart ({highestBeforeRestart}) — the REST path resets "
            + "exactly like the push path does (#2426 spec.md Finding A)");
    }

    // ---- helpers ------------------------------------------------------------

    private sealed record ResolvedFrame(Guid Overlay, string ResolvedText, long Version);

    /// <summary>
    /// Writes a value and returns the frame it produced. The completion
    /// source is replaced before the write because it fires once: without the
    /// swap a later write would be answered by an earlier frame, corrupting
    /// every version comparison that follows.
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
        ListenAsync()
    {
        string accessToken = await aspire.GetAccessTokenAsync(AspireFixture.AdminUsername, AspireFixture.AdminPassword);

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

    /// <summary>
    /// Restarts the service through Aspire, and — whatever happens — leaves
    /// it running. Reused verbatim from
    /// <c>RestartLosesNothingIntegrationTests.cs:84-120</c> (spec 202
    /// tasks.md T001's explicit instruction): the idempotent
    /// <see cref="KnownResourceCommands.StartCommand"/> in the
    /// <c>finally</c> repairs a restart that failed rather than leaving the
    /// service down for every test that runs after this one, and
    /// <see cref="WaitBehavior.WaitOnResourceUnavailable"/> is load-bearing —
    /// the default wait behaviour gives up on exactly the transition a
    /// restart passes through on its way back up (#2038, ADR-0150).
    /// </summary>
    private async Task RestartAsync(string resourceName)
    {
        ResourceCommandService commands = aspire.App.Services.GetRequiredService<ResourceCommandService>();

        try
        {
            ExecuteCommandResult result = await commands.ExecuteCommandAsync(
                resourceName, KnownResourceCommands.RestartCommand, CancellationToken.None);
            result.Success.ShouldBeTrue($"could not restart {resourceName}: {result.Message}");
        }
        finally
        {
            await commands.ExecuteCommandAsync(
                resourceName, KnownResourceCommands.StartCommand, CancellationToken.None);

            await WaitForHealthyAsync(resourceName);
        }
    }

    private async Task WaitForHealthyAsync(string resourceName)
    {
        try
        {
            await aspire.App.ResourceNotifications
                .WaitForResourceHealthyAsync(
                    resourceName, WaitBehavior.WaitOnResourceUnavailable, CancellationToken.None)
                .WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (Exception exception)
        {
            output.WriteLine($"{resourceName} did not become healthy: {exception.Message}");
            output.WriteLine($"---- {resourceName} snapshot ----");
            output.WriteLine(await aspire.ResourceDiagnosticsAsync(resourceName));
            output.WriteLine($"---- {resourceName} log tail ----");
            output.WriteLine(aspire.RecentLogs(resourceName));

            throw;
        }
    }

    /// <summary>
    /// The arrangement both tests share: a munich variable, a published
    /// overlay whose label embeds its placeholder, and a published munich
    /// layout referencing that overlay. Mirrors
    /// <c>ResolvedTextReachesItsFabTests.AMunichOverlayBoundToAVariableAsync</c>.
    /// </summary>
    private async Task<(string VariableName, Guid Overlay)> AMunichOverlayBoundToAVariableAsync()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        string variableName = VariableRequests.UniqueName();
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name = variableName,
            type = "Number",
            initialValue = "0",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();

        Guid overlay = await OverlayRequests.PublishWithLabelAsync(
            overlays, $"Line 1: {{{{{variableName}}}}}", "Rst");

        await PublishAMunichLayoutReferencingAsync(overlay);
        await OverlaySnapshotReadiness.WaitUntilResolvableAsync(variables, overlay, variableName);

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
}
