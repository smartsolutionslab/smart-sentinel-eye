using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 022. The product's purpose is that something happening on the plant
/// floor changes what an operator sees. Until this test, nothing verified that
/// it does.
///
/// <para>
/// Every part of the journey was tested and passing — receiving the event,
/// deciding what it means, setting the value, showing it. What was never tested
/// is the journey itself, and the parts are joined by messages crossing four
/// services. That gap was open, spec 021 broke exactly there, and <b>228
/// integration tests, twenty coverage gates and a green build all passed</b>. A
/// person reading the code found it.
/// </para>
///
/// <para>
/// <b>What this asserts, and what it must never assert.</b> Each of these was
/// true throughout the failure and would have passed against it:
/// <c>FabEventIngestedV1</c> was published; the rule was evaluated; the effects
/// were computed; <c>SystemVariableValueRequestedV1</c> was published — that one
/// <i>is</i> what broke, into a message context nobody flushed; the event was
/// stored and readable, which is what made the break invisible.
/// </para>
///
/// <para>
/// So the assertion is the <b>changed value</b>, read back the way an operator's
/// overlay resolves it. It is the only state downstream of every join.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventReachesItsEffectsTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const string Fab = "munich";
    private const string Topic = "fab/munich/plc/station-4";

    /// <summary>
    /// Four services and a broker. Generous, and bounded — FR-009 wants "late"
    /// and "never" told apart, which a fixed sleep cannot do.
    /// </summary>
    private static readonly TimeSpan EffectDeadline = TimeSpan.FromSeconds(60);

    /// <summary>
    /// FR-001, SC-001. The whole feature in one case.
    /// </summary>
    [Fact]
    public async Task An_event_from_the_plant_floor_changes_the_variable_a_rule_names()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        string variable = $"oee{Guid.CreateVersion7():N}"[..16];
        await DefineVariableAsync(variables, variable, "0");
        output.WriteLine($"defined {variable} = 0");

        // Defined first on purpose: SetVariableValueCommandHandler refuses a
        // variable it cannot find, so without this the rule would fire, the
        // request would be published, and the effect would be refused three
        // contexts away — a failure that looks like a broken journey and is not.
        string rule = await ActivateRuleAsync(rules, variable);
        output.WriteLine($"activated rule {rule}");

        await PublishEventAsync("PlcCycleStart", cycleTime: 10);
        output.WriteLine("published a matching event over the broker");

        string? observed = await WaitForValueAsync(variables, variable, expected: "80");

        observed.ShouldBe(
            "80",
            $"the event never reached the variable. Last read: {observed ?? "<absent>"}. "
            + "The rule was active and the event was published, so the break is in one of "
            + "the joins between EventIngestion, Automation and SystemVariables — which is "
            + "exactly the failure this test exists to catch.");
    }

    /// <summary>
    /// FR-003, SC-003. Without this, a positive assertion that cannot fail is
    /// indistinguishable from a passing one — a test asserting a value equals
    /// what it already was would pass on a completely dead system.
    /// </summary>
    [Fact]
    public async Task An_event_no_rule_matches_changes_nothing()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        string variable = $"idle{Guid.CreateVersion7():N}"[..16];
        await DefineVariableAsync(variables, variable, "7");
        await ActivateRuleAsync(rules, variable, triggerKind: "PlcCycleStart");

        // A kind no active rule triggers on.
        await PublishEventAsync("PlcSomethingElse", cycleTime: 10);
        output.WriteLine("published an event matching no active rule");

        await Task.Delay(TimeSpan.FromSeconds(10));

        string? observed = await ReadValueAsync(variables, variable);
        output.WriteLine($"{variable} after an unmatched event: {observed}");

        observed.ShouldBe("7", "an event no rule matches changed a variable anyway");
    }

    /// <summary>
    /// FR-004's edge case. Redelivery stopped being rare with spec 020 — the
    /// broker now redelivers anything unacknowledged — so the same event
    /// arriving twice is an ordinary case, and SystemVariables dedups by the
    /// causing event.
    /// </summary>
    [Fact]
    public async Task The_same_event_twice_applies_its_effect_once()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        string variable = $"dup{Guid.CreateVersion7():N}"[..16];
        await DefineVariableAsync(variables, variable, "0");
        await ActivateRuleAsync(rules, variable);

        string payload = EventPayload("PlcCycleStart", cycleTime: 10);
        await PublishRawAsync(payload);
        await PublishRawAsync(payload);
        output.WriteLine("published the identical event twice");

        string? observed = await WaitForValueAsync(variables, variable, expected: "80");
        observed.ShouldBe("80", "the effect did not arrive at all");

        // The value is idempotent, so this asserts the weaker but honest thing:
        // a duplicate did not corrupt it. A counter would prove more, and rules
        // do not currently have an action that increments.
        await Task.Delay(TimeSpan.FromSeconds(5));
        (await ReadValueAsync(variables, variable)).ShouldBe(
            "80", "a redelivered event changed the value a second time");
    }

    // ---- arranging -----------------------------------------------------------

    private static async Task DefineVariableAsync(HttpClient variables, string name, string initial)
    {
        HttpResponseMessage defined = await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = initial,
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        defined.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a rule and <b>publishes</b> it, then asserts it reads back Active
    /// (FR-006).
    ///
    /// <para>
    /// <c>POST /rules</c> mints a Draft, and only Active rules are evaluated —
    /// <c>RuleEvaluator</c> reads a cache that <c>PublishRuleCommandHandler</c>
    /// upserts. A test that created a rule and stopped would seed something that
    /// cannot fire, observe no effect, and pass or fail for a reason that has
    /// nothing to do with the journey. Asserting the state here is what makes a
    /// downstream failure attributable.
    /// </para>
    /// </summary>
    private static async Task<string> ActivateRuleAsync(
        HttpClient rules, string variable, string triggerKind = "PlcCycleStart")
    {
        string name = $"reach{Guid.CreateVersion7():N}"[..18];

        HttpResponseMessage created = await rules.PostAsJsonAsync($"/rules?fabId={Fab}", new
        {
            name,
            triggerSource = "plc",
            triggerKind,
            predicate = "$.payload.cycleTime <= 30",
            actionType = "SetVariableValue",
            variableName = variable,
            valueExpression = "100 - $.payload.cycleTime * 2",
            overlayIdentifier = (Guid?)null,
            durationMs = (int?)null,
        });
        created.StatusCode.ShouldBe(
            System.Net.HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        int version = await VersionOfAsync(rules, name);

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish?fabId={Fab}");
        publish.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        HttpResponseMessage published = await rules.SendAsync(publish);
        published.EnsureSuccessStatusCode();

        HttpResponseMessage readBack = await rules.GetAsync($"/rules/{name}?fabId={Fab}");
        readBack.EnsureSuccessStatusCode();
        JsonElement body = await readBack.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("state").GetString().ShouldBe(
            "Active",
            "the rule is not Active, so nothing it says would have happened regardless "
            + "of whether the journey works — a test relying on it would pass or fail "
            + "for the wrong reason");

        return name;
    }

    private static async Task<int> VersionOfAsync(HttpClient rules, string name)
    {
        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}?fabId={Fab}");
        fetched.EnsureSuccessStatusCode();
        JsonElement body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("version").GetInt32();
    }

    // ---- observing -----------------------------------------------------------

    /// <summary>
    /// Polls to a deadline and reports what it last saw, so a failure says
    /// whether the effect was late or absent (FR-009). A fixed sleep would be
    /// slower and would answer neither question.
    /// </summary>
    private async Task<string?> WaitForValueAsync(HttpClient variables, string name, string expected)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + EffectDeadline;
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string? observed = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            observed = await ReadValueAsync(variables, name);
            if (observed == expected)
            {
                output.WriteLine(
                    $"{name} = {observed} after {(DateTimeOffset.UtcNow - started).TotalMilliseconds:F0} ms");
                return observed;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        output.WriteLine($"{name} never reached {expected}; last seen {observed ?? "<absent>"}");
        return observed;
    }

    private static async Task<string?> ReadValueAsync(HttpClient variables, string name)
    {
        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}");
        if (!fetched.IsSuccessStatusCode)
        {
            return null;
        }

        JsonElement body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("value", out JsonElement value) ? value.GetString() : null;
    }

    // ---- the plant floor -----------------------------------------------------

    private Task PublishEventAsync(string kind, int cycleTime) =>
        PublishRawAsync(EventPayload(kind, cycleTime));

    private static string EventPayload(string kind, int cycleTime) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        payload = new { cycleTime },
    });

    /// <summary>
    /// Over MQTT, as a machine sends it (FR-002). Not an HTTP post into the
    /// middle of the chain: the ingress is a join like any other, and a shortcut
    /// past it would leave the first one untested.
    /// </summary>
    private async Task PublishRawAsync(string payload)
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

        MqttClientPublishResult published = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(Topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        published.IsSuccess.ShouldBeTrue("the broker refused the publish");

        await client.DisconnectAsync();
    }
}
