using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 020 T017 — quickstart step 2. The in-memory buffer used to hold up to
/// 5 000 events the system had already said it accepted; losing it lost them,
/// and nothing was even logged.
///
/// <para>
/// <b>Nothing was implemented for this.</b> It passes because an envelope
/// sitting in the channel is no longer something anyone was promised — the
/// broker has not been acknowledged, so it still holds its copy. If this fails,
/// an acknowledgement is still happening too early somewhere.
/// </para>
///
/// <para>
/// The service is restarted through Aspire rather than killed outright, because
/// a project resource is a local process the fixture would not get back. The
/// substitution is honest here and only here: nothing is acknowledged before it
/// is stored, so a graceful stop and a crash leave the broker holding exactly
/// the same set. A real kill is walked by hand in the quickstart, and what it
/// showed is on the PR.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RestartLosesNothingIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const int Published = 500;

    [Fact]
    public async Task A_restart_mid_drain_stores_every_event_exactly_once()
    {
        string kind = $"Restart{Guid.CreateVersion7():N}"[..20];

        await PublishAsync("fab/munich/plc/station-4", kind, Published);
        output.WriteLine($"published {Published} events");

        // Deliberately not waiting for the drain: the whole question is what
        // happens to what is still in flight.
        await RestartAsync("event-ingestion");
        output.WriteLine("restarted event-ingestion mid-drain");

        (long total, long distinct) = await WaitForAsync(kind, Published, TimeSpan.FromMinutes(3));
        output.WriteLine($"after restart: count={total} distinct={distinct}");

        total.ShouldBe(Published, "an event still in the buffer at restart was lost");
        distinct.ShouldBe(total, "a redelivered event was stored twice");
    }

    private async Task RestartAsync(string resourceName)
    {
        ResourceCommandService commands =
            aspire.App.Services.GetRequiredService<ResourceCommandService>();
        ExecuteCommandResult result = await commands.ExecuteCommandAsync(
            resourceName, KnownResourceCommands.RestartCommand, CancellationToken.None);

        result.Success.ShouldBeTrue($"could not restart {resourceName}: {result.Message}");
        await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync(
            resourceName, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    private async Task<(long Total, long Distinct)> WaitForAsync(
        string kind, long expected, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        (long total, long distinct) = (0, 0);

        while (DateTimeOffset.UtcNow < deadline)
        {
            (total, distinct) = await CountAsync(kind);
            if (total >= expected)
            {
                // One more pass, a moment later: a duplicate arriving just
                // behind the last event would be missed by stopping the instant
                // the count is met, and duplicates are half the claim.
                await Task.Delay(TimeSpan.FromSeconds(5));
                return await CountAsync(kind);
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        return (total, distinct);
    }

    private async Task<(long Total, long Distinct)> CountAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        long total = await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
        long distinct = await database.Database
            .SqlQueryRaw<long>(
                "SELECT count(DISTINCT event_id) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
        return (total, distinct);
    }

    private async Task PublishAsync(string topic, string kind, int count)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = SimulatorClientId,
            ["client_secret"] = SimulatorClientSecret,
        });
        HttpResponseMessage token = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        token.EnsureSuccessStatusCode();
        string jwt = (await token.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;

        Uri broker = aspire.App.GetEndpoint("mosquitto", "mqtt");
        using IMqttClient client = new MqttFactory().CreateMqttClient();
        MqttClientConnectResult connected = await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId($"{SimulatorClientId}-{Guid.CreateVersion7():N}")
            .WithCredentials(SimulatorClientId, jwt)
            .WithTcpServer(broker.Host, broker.Port)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .Build());
        connected.ResultCode.ShouldBe(MqttClientConnectResultCode.Success);

        for (int i = 0; i < count; i++)
        {
            MqttClientPublishResult published = await client.PublishAsync(
                new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Payload(kind))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            published.IsSuccess.ShouldBeTrue($"the broker refused publish {i} to {topic}");
        }

        await client.DisconnectAsync();
    }

    private static string Payload(string kind) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        payload = new { note = "spec 020 restart" },
    });
}
