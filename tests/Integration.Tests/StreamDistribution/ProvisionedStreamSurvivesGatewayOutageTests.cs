using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 309 FR-001's premise, against a real transaction rather than a fake
/// repository: <c>ProvisionStreamCommandHandler</c> now calls
/// <c>streams.SaveAsync()</c> before <c>AddPathAsync</c> on the theory that the
/// save commits durably before the gateway is ever asked to add a path. A unit
/// test against <c>InMemoryStreamRepository</c> can prove the handler calls
/// save first; it cannot prove the database agrees, because there is no real
/// transaction behind it (memory: a fake asserting that would be testing the
/// fake).
///
/// <para>
/// Stopping <c>mediamtx</c> for the whole test makes <c>AddPathAsync</c> fail
/// every time — there is nothing listening — so the Stream row's appearance can
/// only be the save. This is the complement to the decompiled-source finding
/// (spec 309 investigation, #2658 item 1): Wolverine's
/// <c>AutoApplyTransactions</c> begins the ambient EF Core transaction via
/// <c>EnrollDbContextInTransaction</c> <em>before</em> the handler runs, and
/// <c>DbContextOutbox&lt;T&gt;.SaveChangesAndFlushMessagesAsync</c> — reached by the
/// mid-handler <c>streams.SaveAsync()</c> — commits that transaction immediately
/// because <c>CurrentTransaction</c> is already non-null, not at the end of the
/// handler. This test is the observable half of that proof: a crash or
/// unreachable gateway after the save cannot un-commit a row that is already
/// durable.
/// </para>
/// </summary>
/// <remarks>
/// <b>Excluded from CI by its category</b>, for the same reason as
/// <c>OutboxSurvivesAKillTests</c>: stopping a resource through Aspire fails
/// outright on the CI runner ("Failed to stop resource"). The resource is
/// restarted in a <c>finally</c> whatever happens, so it cannot poison a later
/// test; the trade is that this fact has no CI coverage and is verified by
/// hand instead.
/// </remarks>
[Collection(AspireCollection.Name)]
[Trait("Category", "Disruptive")]
public class ProvisionedStreamSurvivesGatewayOutageTests(AspireFixture aspire, ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_stream_row_commits_even_when_the_gateway_is_unreachable()
    {
        await StopAsync("mediamtx");
        output.WriteLine("stopped mediamtx -- AddPathAsync cannot succeed from here on");

        try
        {
            using HttpClient cameraClient = await aspire.CreateAdminClientAsync("camera-catalog");

            HttpResponseMessage register = await cameraClient.PostAsJsonAsync(
                "/cameras",
                new { name = "Gateway-Outage", rtspUrl = "rtsp://unreachable.test/h264" });
            register.StatusCode.ShouldBe(HttpStatusCode.Created);
            Guid camera = await register.Content.ReadFromJsonAsync<Guid>();
            camera.ShouldNotBe(Guid.Empty);

            // Observed through a DbContext the handler never touches -- a fresh
            // connection against the same Postgres instance -- so a row seen
            // here is the database's own commit, not anything this test's
            // writer cached.
            Stream? stream = await WaitForStreamRowAsync(camera, StreamTimeout);

            stream.ShouldNotBeNull(
                "the Stream row never committed, even though the only thing that ran " +
                "after the save -- AddPathAsync against a stopped mediamtx -- cannot " +
                "have succeeded");
            stream.Path.Value.ShouldBe($"cam-{camera}");
            output.WriteLine($"stream row for {camera} is durable with mediamtx stopped");
        }
        finally
        {
            await StartAsync("mediamtx");
        }
    }

    private async Task<Stream?> WaitForStreamRowAsync(Guid camera, TimeSpan timeout)
    {
        CameraIdentifier cameraId = CameraIdentifier.From(camera);
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await using StreamDistributionDbContext context =
                await aspire.CreateStreamDistributionDbContextAsync();
            Stream? found = await context.Streams
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Camera == cameraId);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(PollInterval);
        }

        return null;
    }

    private async Task StopAsync(string resourceName)
    {
        ResourceCommandService commands =
            aspire.App.Services.GetRequiredService<ResourceCommandService>();

        ExecuteCommandResult result = await commands.ExecuteCommandAsync(
            resourceName, KnownResourceCommands.StopCommand, CancellationToken.None);
        result.Success.ShouldBeTrue($"could not stop {resourceName}: {result.Message}");
    }

    /// <summary>
    /// Restarts, and leaves the resource running whatever happens -- the same
    /// shape as <c>OutboxSurvivesAKillTests.RestartAsync</c>, for the same
    /// reason: a failed restore here must not poison every later test in the
    /// collection with an unrelated "mediamtx unreachable" failure.
    /// </summary>
    private async Task StartAsync(string resourceName)
    {
        ResourceCommandService commands =
            aspire.App.Services.GetRequiredService<ResourceCommandService>();

        await commands.ExecuteCommandAsync(resourceName, KnownResourceCommands.StartCommand, CancellationToken.None);

        await aspire.App.ResourceNotifications
            .WaitForResourceHealthyAsync(
                resourceName, WaitBehavior.WaitOnResourceUnavailable, CancellationToken.None)
            .WaitAsync(TimeSpan.FromMinutes(2));
    }
}
