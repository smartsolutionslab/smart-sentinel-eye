using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 021 T024, SC-002. A crash between committing the rows and releasing the
/// messages must not lose the announcements — they are in the outbox, in the
/// same transaction as the rows, so the recovery agent has them.
///
/// <para>
/// This is the claim that most distinguishes the outbox from the thing it
/// replaced. Before, the window between commit and publish was in memory: a
/// process that died inside it took the announcement with it and left no trace.
/// Now the window's contents are rows.
/// </para>
/// </summary>
/// <remarks>
/// <b>Excluded from CI by its category</b>, for the reason spec 020 recorded
/// against the same mechanism: restarting a resource through Aspire fails
/// outright on the CI runner ("Failed to stop resource"), and on its first CI
/// run that failure left the service down and eleven later tests failed with
/// socket errors. The resource is restored in a finally whatever happens, so it
/// can no longer poison a run — but a test that goes red on a platform
/// limitation rather than a defect teaches people to ignore red CI. The cost is
/// stated rather than implied: SC-002 has no CI coverage here either.
/// </remarks>
[Collection(AspireCollection.Name)]
[Trait("Category", "Disruptive")]
public class OutboxSurvivesAKillTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Operator = "op-hamburg@hamburg.test";
    private const string OperatorPassword = "Operator1234";
    private const string OutboxSchema = "wolverine_event_ingestion";
    private const int Written = 40;

    [Fact]
    public async Task Announcements_committed_before_a_restart_are_delivered_after_it()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", Operator, OperatorPassword);

        string kind = $"Killed{Guid.CreateVersion7():N}"[..20];

        for (int i = 0; i < Written; i++)
        {
            HttpResponseMessage created = await client.PostAsJsonAsync("/events/manual", new
            {
                deviceId = "kill-device",
                kind,
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "spec 021 T024", sequence = i },
            });
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        output.WriteLine($"wrote {Written} events, each committed with its announcement");

        // Deliberately not waiting for the outbox to drain: the question is what
        // happens to announcements that are committed and not yet released.
        await RestartAsync("event-ingestion");
        output.WriteLine("restarted event-ingestion");

        (await StoredAsync(kind)).ShouldBe(Written, "a committed event went missing across the restart");

        long pending = await DrainedAsync(TimeSpan.FromMinutes(2));
        output.WriteLine($"pending announcements after restart: {pending}");

        pending.ShouldBe(
            0,
            "announcements committed before the restart were not delivered after it — "
            + "the recovery agent is not picking up what the outbox holds");
    }

    /// <summary>
    /// Restarts, and leaves the resource running whatever happens. Spec 020
    /// learned this the expensive way: without the finally, a restart that fails
    /// on the runner leaves the service down and every later test in the
    /// collection fails with an unrelated socket error, burying whichever one
    /// actually found something.
    /// </summary>
    private async Task RestartAsync(string resourceName)
    {
        ResourceCommandService commands =
            aspire.App.Services.GetRequiredService<ResourceCommandService>();

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

            // WaitOnResourceUnavailable, for the reason set out in
            // RestartLosesNothingIntegrationTests: a restart passes through an
            // unavailable state, and the default treats reaching one as a reason
            // to stop waiting — abandoning the resource for the very transition
            // the wait exists to watch (#2038).
            await aspire.App.ResourceNotifications
                .WaitForResourceHealthyAsync(
                    resourceName, WaitBehavior.WaitOnResourceUnavailable, CancellationToken.None)
                .WaitAsync(TimeSpan.FromMinutes(2));
        }
    }

    private async Task<long> DrainedAsync(TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        long pending = await PendingAsync();

        while (pending > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            pending = await PendingAsync();
        }

        return pending;
    }

    private async Task<long> PendingAsync()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>(
                $"SELECT count(*) AS \"Value\" FROM {OutboxSchema}.wolverine_outgoing_envelopes")
            .SingleAsync();
    }

    private async Task<long> StoredAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
    }
}
