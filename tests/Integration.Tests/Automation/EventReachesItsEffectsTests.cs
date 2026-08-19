using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
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
/// So the assertions are the two <b>effects</b> — the changed value, read back
/// the way an operator's overlay resolves it, and the highlight frame, taken at
/// the hub a kiosk connects to. They are the only state downstream of every
/// join, and they travel to different contexts by different routes, so covering
/// one proves nothing about the other.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventReachesItsEffectsTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Fab = "munich";

    /// <summary>How long a highlight stays lit — asserted, so the frame is this rule's.</summary>
    private const int HighlightMs = 2_500;

    private readonly PlantFloor plant = new(aspire);

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

        string variable = $"oee{Guid.NewGuid():N}"[..16];
        await DefineVariableAsync(variables, variable, "0");
        output.WriteLine($"defined {variable} = 0");

        // Defined first on purpose: SetVariableValueCommandHandler refuses a
        // variable it cannot find, so without this the rule would fire, the
        // request would be published, and the effect would be refused three
        // contexts away — a failure that looks like a broken journey and is not.
        string rule = await ActivateRuleAsync(rules, variable);
        output.WriteLine($"activated rule {rule}");

        await plant.PublishAsync("PlcCycleStart", cycleTime: 10);
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
    /// FR-002, SC-001 — the other effect. It leaves Automation on a different
    /// message, lands in a different context, and arrives over SignalR rather
    /// than a read API, so the value case proves nothing about it.
    ///
    /// <para>
    /// Taken at the hub because that is where the product's own boundary is: the
    /// kiosk applying a CSS class is the browser's job and the e2e suite's. The
    /// overlay identifier need not exist — a highlight is addressed to a fab
    /// group, not resolved against a layout — so the frame is matched on the
    /// identifier and the duration the rule named, which is what distinguishes
    /// it from any other highlight the fixture happens to be carrying.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_event_from_the_plant_floor_highlights_the_overlay_a_rule_names()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        Guid overlay = Guid.CreateVersion7();
        string rule = await ActivateHighlightRuleAsync(rules, overlay);
        output.WriteLine($"activated highlight rule {rule} for overlay {overlay}");

        TaskCompletionSource<int> highlighted = new();
        await using HubConnection kiosk = await ListenForHighlightAsync(overlay, highlighted);

        // Connected before the event, not after: the frame is pushed once and
        // not replayed, so a listener that joins late hears nothing and reports
        // a working journey as broken.
        await plant.PublishAsync("PlcCycleStart", cycleTime: 10);
        output.WriteLine("published a matching event over the broker");

        DateTimeOffset started = DateTimeOffset.UtcNow;
        using CancellationTokenSource budget = new(EffectDeadline);

        int durationMs = 0;
        bool arrived = true;
        try
        {
            durationMs = await highlighted.Task.WaitAsync(budget.Token);
        }
        catch (OperationCanceledException)
        {
            arrived = false;
        }

        arrived.ShouldBeTrue(
            "the highlight never reached the hub. The rule was active and the event was "
            + "published, so the break is in one of the joins between EventIngestion, "
            + "Automation and LayoutComposition — which is exactly the failure this test "
            + "exists to catch.");

        output.WriteLine(
            $"overlay {overlay} highlighted after {(DateTimeOffset.UtcNow - started).TotalMilliseconds:F0} ms");

        durationMs.ShouldBe(
            HighlightMs, "the frame arrived carrying a duration no rule in this test asked for");
    }

    /// <summary>
    /// FR-003, SC-003. Without this, a positive assertion that cannot fail is
    /// indistinguishable from a passing one — a test asserting a value equals
    /// what it already was would pass on a completely dead system.
    ///
    /// <para>
    /// <b>Two rules and one event, rather than a rule and a wait.</b> The
    /// obvious shape — publish something nothing matches, sleep, assert nothing
    /// changed — cannot fail for the right reason. It reports "unchanged" on a
    /// dead broker, an unpersisted event, or simply a stack slower than the
    /// sleep, and this suite measures the first event of a run at 12–14 s. The
    /// matching rule removes all three: both effects are fanned out from the
    /// same event in the same pass, so once the matched variable has moved, the
    /// ignored one has had its chance and did not take it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_event_changes_only_the_variable_whose_rule_matches_it()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        string matched = $"hit{Guid.NewGuid():N}"[..16];
        string ignored = $"idle{Guid.NewGuid():N}"[..16];
        await DefineVariableAsync(variables, matched, "0");
        await DefineVariableAsync(variables, ignored, "7");

        await ActivateRuleAsync(rules, matched);
        await ActivateRuleAsync(rules, ignored, triggerKind: "PlcSomethingElse");

        await plant.PublishAsync("PlcCycleStart", cycleTime: 10);
        output.WriteLine("published one event that matches one of the two active rules");

        // The sync point, and the proof the system is alive at all.
        (await WaitForValueAsync(variables, matched, expected: "80")).ShouldBe(
            "80", "the matching rule's effect never arrived, so this case establishes nothing");

        string? observed = await ReadValueAsync(variables, ignored);
        output.WriteLine($"{ignored} after an event its rule does not match: {observed}");

        observed.ShouldBe("7", "an event no rule matches changed a variable anyway");
    }

    /// <summary>
    /// FR-004's edge case. Redelivery stopped being rare with spec 020 — the
    /// broker now redelivers anything unacknowledged — so the same event
    /// arriving twice is an ordinary case rather than an exotic one.
    ///
    /// <para>
    /// <b>What this does and does not establish.</b> The duplicate is stopped at
    /// the first join: <c>IngestEventCommandHandler</c> checks
    /// <c>ExistsAsync(fab, identifier)</c> and returns <c>EventAlreadyIngested</c>
    /// <i>before</i> raising <c>FabEventIngestedV1</c>, so it never reaches
    /// Automation or SystemVariables. This therefore covers ingestion
    /// idempotency end to end, and says nothing about SystemVariables' own
    /// dedup-by-causing-event, which would need a duplicate that gets past
    /// ingestion to exercise. Said here because a test whose name promises more
    /// than it checks is the failure mode this whole feature exists to fix.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_redelivered_event_applies_its_effect_once()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");

        string variable = $"dup{Guid.NewGuid():N}"[..16];
        await DefineVariableAsync(variables, variable, "0");
        await ActivateRuleAsync(rules, variable);

        string payload = PlantFloor.Payload("PlcCycleStart", cycleTime: 10);
        await plant.PublishRawAsync(payload);
        await plant.PublishRawAsync(payload);
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
    private static Task<string> ActivateRuleAsync(
        HttpClient rules, string variable, string triggerKind = "PlcCycleStart")
    {
        string name = $"reach{Guid.NewGuid():N}"[..18];

        return ActivateAsync(rules, name, new
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
    }

    /// <summary>
    /// The same lifecycle, the other action. A highlight names an overlay and a
    /// duration instead of a variable and an expression, and leaves by a
    /// different route to a different context.
    /// </summary>
    private static Task<string> ActivateHighlightRuleAsync(HttpClient rules, Guid overlay)
    {
        string name = $"light{Guid.NewGuid():N}"[..18];

        return ActivateAsync(rules, name, new
        {
            name,
            triggerSource = "plc",
            triggerKind = "PlcCycleStart",
            predicate = "$.payload.cycleTime <= 30",
            actionType = "HighlightOverlay",
            variableName = (string?)null,
            valueExpression = (string?)null,
            overlayIdentifier = (Guid?)overlay,
            durationMs = (int?)HighlightMs,
        });
    }

    private static async Task<string> ActivateAsync(HttpClient rules, string name, object definition)
    {
        HttpResponseMessage created = await rules.PostAsJsonAsync($"/rules?fabId={Fab}", definition);
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

    /// <summary>
    /// Connects to the hub a kiosk connects to, as an operator holding the fab.
    /// The hub joins each of the caller's fabs to a group on connect, and the
    /// highlight is addressed to the rule's fab — so a token without it would
    /// see nothing and read exactly like a broken journey.
    /// </summary>
    private async Task<HubConnection> ListenForHighlightAsync(
        Guid overlay, TaskCompletionSource<int> highlighted)
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
            frame =>
            {
                if (frame.GetProperty("overlay").GetGuid() == overlay)
                {
                    highlighted.TrySetResult(frame.GetProperty("durationMs").GetInt32());
                }
            });

        await kiosk.StartAsync();
        return kiosk;
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

}
