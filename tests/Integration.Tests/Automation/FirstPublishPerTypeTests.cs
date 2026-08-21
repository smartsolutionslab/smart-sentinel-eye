using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 023 T013. Tests the one hypothesis that predicts the observed shape —
/// that the cost is paid once per <b>message type</b> on its first publish, not
/// once per process — by arranging which type goes first rather than by reading
/// spans.
///
/// <para>
/// <b>The design is a discriminator, not a demonstration.</b> Four events in one
/// run, each with its own trigger kind so exactly one rule fires:
/// </para>
///
/// <list type="table">
///   <item><term>A — highlight</term><description>first <c>FabEventIngestedV1</c> and first <c>OverlayHighlightRequestedV1</c></description></item>
///   <item><term>B — variable</term><description>first <c>SystemVariableValueRequestedV1</c>; everything else already warm</description></item>
///   <item><term>C — variable</term><description>nothing new</description></item>
///   <item><term>D — highlight</term><description>nothing new</description></item>
/// </list>
///
/// <para>
/// <b>B decides it.</b> If the cost is per message type, B is slow even though a
/// full event has already completed before it. If the cost is simply "the first
/// event pays for everything", B is fast and the hypothesis is dead. C and D
/// bound the answer from the other side: whatever B costs, its repeat must be
/// cheap, or the effect is not about firstness at all.
/// </para>
///
/// <para>
/// No production change and no startup warming: warming the path before knowing
/// what it costs is exactly what spec 023 exists to avoid.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
[Trait("Category", "Measurement")]
public class FirstPublishPerTypeTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Fab = "munich";
    private const int HighlightMs = 2_500;

    private readonly PlantFloor plant = new(aspire);

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Whether_the_cost_is_paid_once_per_message_type()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        // Connected once, before anything is published, so hub setup is not
        // charged to whichever highlight round happens to be first.
        ConcurrentFrames frames = new();
        await using HubConnection kiosk = await ListenAsync(frames);

        // Which queues exist before anything is published decides whether the
        // per-type cost can be broker provisioning at all: a queue that already
        // exists cannot be created by the publish that follows it.
        output.WriteLine("queues before any publish: " + await QueueNamesAsync());
        output.WriteLine("");
        output.WriteLine("round | kind      | new message type(s)                    | arrival -> effect");

        await HighlightRoundAsync(rules, kiosk: frames, "A", "FabEventIngestedV1 + OverlayHighlightRequestedV1");
        await VariableRoundAsync(variables, rules, "B", "SystemVariableValueRequestedV1");
        await VariableRoundAsync(variables, rules, "C", "none");
        await HighlightRoundAsync(rules, kiosk: frames, "D", "none");

        output.WriteLine("");
        output.WriteLine("If B is slow, the cost is per message type. If B is fast, it is not.");
    }

    private async Task VariableRoundAsync(
        HttpClient variables, HttpClient rules, string round, string newTypes)
    {
        string variable = $"perty{Guid.NewGuid():N}"[..16];
        string kind = $"PlcKind{round}";
        await DefineVariableAsync(variables, variable);
        await ActivateVariableRuleAsync(rules, variable, kind);

        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        await plant.PublishAsync(kind, cycleTime: 10);

        TimeSpan? applied = await WaitForAsync(async () =>
        {
            HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{variable}");
            if (!fetched.IsSuccessStatusCode)
            {
                return false;
            }

            JsonElement body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
            return body.TryGetProperty("value", out JsonElement value) && value.GetString() == "80";
        }, t0);

        Report(round, kind, newTypes, applied);
    }

    private async Task HighlightRoundAsync(
        HttpClient rules, ConcurrentFrames kiosk, string round, string newTypes)
    {
        Guid overlay = Guid.NewGuid();
        string kind = $"PlcKind{round}";
        await ActivateHighlightRuleAsync(rules, overlay, kind);

        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        await plant.PublishAsync(kind, cycleTime: 10);

        TimeSpan? seen = await WaitForAsync(() => Task.FromResult(kiosk.Contains(overlay)), t0);

        Report(round, kind, newTypes, seen);
    }

    /// <summary>
    /// Asks the broker which queues exist, through the management plugin. The
    /// discriminator this run turns on: broker provisioning and code generation
    /// both cost time once per type, but only one of them can be ruled out by a
    /// queue that already exists before the publish that would have created it.
    /// </summary>
    private async Task<string> QueueNamesAsync()
    {
        Uri management = aspire.App.GetEndpoint("rabbitmq", "management");

        // Credentials off the connection string rather than guessed: the broker
        // user is whatever the AppHost parameterised, and a 401 here would look
        // exactly like "no queues" while meaning "no answer".
        string connection = await aspire.App.GetConnectionStringAsync("rabbitmq") ?? "";
        string userInfo = new Uri(connection).UserInfo;

        using HttpClient client = new() { BaseAddress = management };
        client.DefaultRequestHeaders.Authorization = new("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(userInfo))));

        HttpResponseMessage response = await client.GetAsync("/api/queues");
        if (!response.IsSuccessStatusCode)
        {
            return "(management API said " + (int)response.StatusCode + ")";
        }

        JsonElement queues = await response.Content.ReadFromJsonAsync<JsonElement>();
        List<string> names = [];
        foreach (JsonElement queue in queues.EnumerateArray())
        {
            string name = queue.GetProperty("name").GetString() ?? "";
            if (name.Contains("V1", StringComparison.Ordinal))
            {
                names.Add(name[(name.LastIndexOf('.') + 1)..]);
            }
        }

        return names.Count == 0 ? "(none carrying a message type)" : string.Join(", ", names.Order());
    }

    private void Report(string round, string kind, string newTypes, TimeSpan? elapsed) =>
        output.WriteLine(
            $"  {round}   | {kind,-9} | {newTypes,-38} | "
            + (elapsed is null ? "never" : $"{elapsed.Value.TotalMilliseconds,8:F0} ms"));

    private static async Task<TimeSpan?> WaitForAsync(Func<Task<bool>> observed, DateTimeOffset from)
    {
        DateTimeOffset deadline = from + Deadline;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await observed())
            {
                return DateTimeOffset.UtcNow - from;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return null;
    }

    private async Task<HubConnection> ListenAsync(ConcurrentFrames frames)
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        HubConnection kiosk = new HubConnectionBuilder()
            .WithUrl(
                aspire.HubUri("layout-composition", LayoutLifecycleHub.Path),
                options => options.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .Build();

        kiosk.On<JsonElement>(
            nameof(ILayoutLifecycleClient.OverlayHighlightChanged),
            frame => frames.Add(frame.GetProperty("overlay").GetGuid()));

        await kiosk.StartAsync();
        return kiosk;
    }

    private sealed class ConcurrentFrames
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> seen = new();

        public void Add(Guid overlay) => seen.TryAdd(overlay, 0);

        public bool Contains(Guid overlay) => seen.ContainsKey(overlay);
    }

    private static async Task DefineVariableAsync(HttpClient variables, string name)
    {
        HttpResponseMessage defined = await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = "0",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        defined.EnsureSuccessStatusCode();
    }

    private static Task ActivateVariableRuleAsync(HttpClient rules, string variable, string kind) =>
        ActivateAsync(rules, new
        {
            name = $"pv{Guid.NewGuid():N}"[..18],
            triggerSource = "plc",
            triggerKind = kind,
            predicate = "$.payload.cycleTime <= 30",
            actionType = "SetVariableValue",
            variableName = variable,
            valueExpression = "100 - $.payload.cycleTime * 2",
            overlayIdentifier = (Guid?)null,
            durationMs = (int?)null,
        });

    private static Task ActivateHighlightRuleAsync(HttpClient rules, Guid overlay, string kind) =>
        ActivateAsync(rules, new
        {
            name = $"ph{Guid.NewGuid():N}"[..18],
            triggerSource = "plc",
            triggerKind = kind,
            predicate = "$.payload.cycleTime <= 30",
            actionType = "HighlightOverlay",
            variableName = (string?)null,
            valueExpression = (string?)null,
            overlayIdentifier = (Guid?)overlay,
            durationMs = (int?)HighlightMs,
        });

    private static async Task ActivateAsync(HttpClient rules, dynamic definition)
    {
        string name = definition.name;

        HttpResponseMessage created = await rules.PostAsJsonAsync($"/rules?fabId={Fab}", (object)definition);
        created.EnsureSuccessStatusCode();

        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}?fabId={Fab}");
        fetched.EnsureSuccessStatusCode();
        int version = (await fetched.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish?fabId={Fab}");
        publish.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        (await rules.SendAsync(publish)).EnsureSuccessStatusCode();
    }
}
