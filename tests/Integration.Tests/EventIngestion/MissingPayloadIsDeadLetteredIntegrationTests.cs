using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 173 (#2203) — a delivery whose <c>payload</c> property is absent
/// leaves that <c>JsonElement</c> at <c>JsonValueKind.Undefined</c>, and
/// <c>GetRawText()</c> on <c>Undefined</c> throws
/// <see cref="InvalidOperationException"/>, which
/// <c>MqttSubscriberHostedService.TryParseEnvelope</c>'s payload-block catch
/// (<c>catch (ArgumentException ex)</c>) does not catch. The escape happens
/// before acknowledgement, so the delivery is never ACKed and QoS 1
/// redelivers it forever — the poison-message escape hatch
/// (<see cref="PoisonDeliveryEscapeIntegrationTests"/>) is bypassed entirely
/// because dead-lettering lives on the path the exception jumps over.
///
/// <para>
/// Modeled on <see cref="PoisonDeliveryEscapeIntegrationTests"/>'s
/// publish/poll shape (that file is untouched — SC-6 — copied here rather
/// than reused). Unlike that test's poison, this delivery's fab
/// (<c>hamburg</c>) has healthy storage throughout: the escape here happens
/// during parse, before the persistence path is ever reached.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MissingPayloadIsDeadLetteredIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const string Fab = "hamburg";
    private const int HealthyCount = 20;

    [Fact]
    public async Task A_delivery_whose_payload_property_is_absent_is_dead_lettered_once_and_released()
    {
        string device = $"dev-{Guid.CreateVersion7():N}"[..20];
        string topic = $"fab/{Fab}/plc/{device}";
        string healthyKind = $"Healthy{Guid.CreateVersion7():N}"[..20];

        await PublishRawAsync(topic, MissingPayloadBody());
        output.WriteLine($"published 1 payload-less delivery to {topic}");

        await PublishHealthyAsync($"fab/{Fab}/plc/station-{Guid.CreateVersion7():N}"[..40], healthyKind, HealthyCount);
        output.WriteLine($"published {HealthyCount} well-formed events");

        string error = await WaitForDeadLetterAsync(topic, TimeSpan.FromSeconds(60));
        output.WriteLine($"dead letter: '{error}'");
        error.ShouldNotBeNullOrEmpty("no dead_letters row ever appeared for the payload-less delivery");
        error.ShouldContain("payload", Case.Insensitive);

        long healthy = await WaitForCountAsync(healthyKind, HealthyCount, TimeSpan.FromSeconds(45));
        output.WriteLine($"healthy stored: {healthy}");
        healthy.ShouldBe(
            HealthyCount, "the payload-less delivery held up the healthy events behind it");

        // Settle, then confirm no second row appeared for the same delivery.
        await Task.Delay(TimeSpan.FromSeconds(10));
        (await CountDeadLettersAsync(topic)).ShouldBe(
            1, "the payload-less delivery was dead-lettered more than once");
    }

    private async Task<long> WaitForCountAsync(string kind, long expected, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        long count = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            count = await CountAsync(kind);
            if (count >= expected)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return count;
    }

    private async Task<string> WaitForDeadLetterAsync(string topic, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using EventIngestionDbContext database =
                await aspire.CreateEventIngestionDbContextAsync();
            string[] found = await database.Database
                .SqlQueryRaw<string>(
                    "SELECT error AS \"Value\" FROM dead_letters WHERE topic = {0}", topic)
                .ToArrayAsync();

            if (found.Length > 0)
            {
                return found[0];
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        return string.Empty;
    }

    private async Task<long> CountDeadLettersAsync(string topic)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM dead_letters WHERE topic = {0}", topic)
            .SingleAsync();
    }

    private async Task<long> CountAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
    }

    /// <summary>
    /// Connects and publishes one raw JSON body at QoS 1, unedited — the
    /// caller controls the shape, so a payload-less delivery can be
    /// constructed directly.
    /// </summary>
    private async Task PublishRawAsync(string topic, string rawJson)
    {
        using IMqttClient client = await ConnectAsync();

        MqttClientPublishResult published = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(rawJson)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        published.IsSuccess.ShouldBeTrue($"the broker refused publish to {topic}");

        await client.DisconnectAsync();
    }

    private async Task PublishHealthyAsync(string topic, string kind, int count)
    {
        using IMqttClient client = await ConnectAsync();

        for (int i = 0; i < count; i++)
        {
            MqttClientPublishResult published = await client.PublishAsync(
                new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(HealthyBody(kind))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            published.IsSuccess.ShouldBeTrue($"the broker refused publish {i} to {topic}");
        }

        await client.DisconnectAsync();
    }

    private async Task<IMqttClient> ConnectAsync()
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
        IMqttClient client = new MqttClientFactory().CreateMqttClient();
        MqttClientConnectResult connected = await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId($"{SimulatorClientId}-{Guid.CreateVersion7():N}")
            .WithCredentials(SimulatorClientId, jwt)
            .WithTcpServer(broker.Host, broker.Port)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build());
        connected.ResultCode.ShouldBe(MqttClientConnectResultCode.Success);

        return client;
    }

    private static string MissingPayloadBody() => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind = "PayloadAbsent",
        occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    });

    private static string HealthyBody(string kind) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        payload = new { note = $"spec 173 healthy {kind}" },
    });
}
