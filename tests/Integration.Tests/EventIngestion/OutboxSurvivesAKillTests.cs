using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 021 T024, SC-002, re-pointed by ADR-0185 (item 5). A crash between committing the
/// rows and releasing the messages must not lose the announcements — they are in the outbox, in the
/// same transaction as the rows, so the recovery agent has them.
///
/// <para>
/// This is the claim that most distinguishes the outbox from the thing it
/// replaced. Before, the window between commit and publish was in memory: a
/// process that died inside it took the announcement with it and left no trace.
/// Now the window's contents are rows.
/// </para>
///
/// <para>
/// <b>Why it publishes <c>WebhookIntegrationRevokedV1</c> and no longer ingests events.</b> The first
/// version asserted that the outgoing table drains to zero after a restart. An Inline sender writes
/// no row, so the table was zero before, during and after and the test could not fail. The event
/// ingest path publishes <c>FabEventIngestedV1</c>, which stays Inline by decision (the hot path,
/// #3103); a revoked integration's announcement is declared durable, so it is the type whose row
/// can be observed pending. To make it pending deterministically the broker is stopped first: the
/// revocation still commits, the send cannot complete, and the row has to survive the restart.
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
    private const string OutboxSchema = "wolverine_event_ingestion";
    private const string Broker = "rabbitmq";
    private const string Announcement = "WebhookIntegrationRevokedV1";

    [Fact]
    public async Task A_revocation_committed_while_the_broker_is_down_is_delivered_after_a_restart_and_the_broker_returning()
    {
        using var client = await aspire.CreateAdminClientAsync("event-ingestion");
        var name = $"kill-{Guid.NewGuid():N}"[..24];

        try
        {
            await StopBrokerAsync();

            var created = await client.PostAsJsonAsync("/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
            created.StatusCode.ShouldBe(HttpStatusCode.Created);

            await RevokeAsync(client, name);
            output.WriteLine($"revoked {name} with the broker stopped");

            // The row is there because the sender is durable. An Inline sender would already have
            // discarded the announcement with a logged "Unable to send outgoing message".
            (await PendingAsync()).ShouldBeGreaterThan(
                0,
                "the revocation's announcement left no outgoing row, so a crash now would lose it");

            // Deliberately not draining first: the question is what happens to announcements that
            // are committed and not yet released.
            // event-ingestion waits for the broker (AppHost WaitFor), so it cannot report healthy
            // until the broker is back: restart without the health wait, return the broker, then wait.
            await RestartAsync("event-ingestion", awaitHealthy: false);
            output.WriteLine("restarted event-ingestion");

            await StartBrokerAsync();
            await WaitForHealthyAsync("event-ingestion");
        }
        finally
        {
            await StartBrokerAsync();
        }

        var pending = await DrainedAsync(TimeSpan.FromMinutes(2));
        output.WriteLine($"pending {Announcement} rows after restart and broker return: {pending}");

        pending.ShouldBe(
            0,
            "an announcement committed before the restart was not delivered after it — "
            + "the recovery agent is not picking up what the outbox holds");
    }

    private static async Task RevokeAsync(HttpClient client, string name)
    {
        var listed = await client.GetAsync("/webhook-integrations?includeRevoked=false");
        listed.EnsureSuccessStatusCode();

        var rows = await listed.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var version = rows.EnumerateArray()
            .Single(row => string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            .GetProperty("version").GetInt32();

        using HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var revoked = await client.SendAsync(request);

        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
    }

    private async Task StopBrokerAsync()
    {
        var commands = aspire.App.Services.GetRequiredService<ResourceCommandService>();
        var stopped = await commands.ExecuteCommandAsync(
            Broker, KnownResourceCommands.StopCommand, CancellationToken.None);
        stopped.Success.ShouldBeTrue($"could not stop {Broker}: {stopped.Message}");
    }

    /// <summary>
    /// Idempotent on a running resource, so it is both the intended second half and the repair after
    /// a stop that failed: the broker is shared by every other test in the collection.
    /// </summary>
    private async Task StartBrokerAsync()
    {
        var commands = aspire.App.Services.GetRequiredService<ResourceCommandService>();
        await commands.ExecuteCommandAsync(Broker, KnownResourceCommands.StartCommand, CancellationToken.None);
        await WaitForHealthyAsync(Broker);
    }

    /// <summary>
    /// Restarts, and leaves the resource running whatever happens. Spec 020
    /// learned this the expensive way: without the finally, a restart that fails
    /// on the runner leaves the service down and every later test in the
    /// collection fails with an unrelated socket error, burying whichever one
    /// actually found something.
    /// </summary>
    private async Task RestartAsync(string resourceName, bool awaitHealthy = true)
    {
        var commands =
            aspire.App.Services.GetRequiredService<ResourceCommandService>();

        try
        {
            var result = await commands.ExecuteCommandAsync(
                resourceName, KnownResourceCommands.RestartCommand, CancellationToken.None);
            result.Success.ShouldBeTrue($"could not restart {resourceName}: {result.Message}");
        }
        finally
        {
            await commands.ExecuteCommandAsync(
                resourceName, KnownResourceCommands.StartCommand, CancellationToken.None);

            if (awaitHealthy)
            {
                await WaitForHealthyAsync(resourceName);
            }
        }
    }

    /// <summary>
    /// WaitOnResourceUnavailable, for the reason set out in
    /// RestartLosesNothingIntegrationTests: a restart passes through an
    /// unavailable state, and the default treats reaching one as a reason
    /// to stop waiting — abandoning the resource for the very transition
    /// the wait exists to watch (#2038).
    /// </summary>
    private async Task WaitForHealthyAsync(string resourceName) =>
        await aspire.App.ResourceNotifications
            .WaitForResourceHealthyAsync(
                resourceName, WaitBehavior.WaitOnResourceUnavailable, CancellationToken.None)
            .WaitAsync(TimeSpan.FromMinutes(2));

    private async Task<long> DrainedAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var pending = await PendingAsync();

        while (pending > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            pending = await PendingAsync();
        }

        return pending;
    }

    private async Task<long> PendingAsync()
    {
        await using var database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>(
                $"SELECT count(*) AS \"Value\" FROM {OutboxSchema}.wolverine_outgoing_envelopes WHERE message_type LIKE {{0}}",
                $"%{Announcement}%")
            .SingleAsync();
    }
}
