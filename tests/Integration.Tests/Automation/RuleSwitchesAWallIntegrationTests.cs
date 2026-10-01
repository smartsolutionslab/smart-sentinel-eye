using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests.Automation;

/// <summary>
/// Spec 296 T115, against the real Aspire stack (ADR-0103). Covers US1/US2
/// from spec.md §4's Gherkin block that are decisive at this layer:
/// US2-1 (round-trip, both targets), US2-2 (the whole journey: switch,
/// audit pair, hub frame), US2-3 (Next), US2-4/US2-5 (PD-1 last-writer-wins
/// in both directions), US2-6 (the race invariant), US2-7 (bad shapes),
/// US2-8 (cross-fab drop), US2-11 (403), US2-12/US2-13 (stale-scene drops).
/// US2-9/US2-10/US2-14 are Application-level only (plan.md §5's table) —
/// already covered by <c>WallSceneSwitchRequestedV1HandlerTests</c>.
///
/// <para>
/// Ingests via <c>POST /events/manual</c> rather than <c>PlantFloor</c>'s
/// MQTT path: the manual endpoint accepts an explicit <c>?fabId=</c>, which
/// US2-8 needs to fire a rule in a fab other than the wall's, and
/// <c>ManualIngestFabScopingIntegrationTests</c> already proves it requires
/// no pre-registered event source.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RuleSwitchesAWallIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    private static readonly TimeSpan EffectDeadline = TimeSpan.FromSeconds(60);

    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
        await aspire.ResetAutomationAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- US2-1: both targets round-trip ---------------------------------------

    [Fact]
    public async Task US2_1_A_SwitchWallScene_rule_with_target_Layout_round_trips()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();

        string name = UniqueRuleName();
        HttpResponseMessage created = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(name, kind, "$.payload.line == 3", wall, "Layout", b));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        JsonElement action = (await ReadRuleAsync(rules, name)).GetProperty("action");
        action.GetProperty("kind").GetString().ShouldBe("SwitchWallScene");
        action.GetProperty("wall").GetGuid().ShouldBe(wall);
        action.GetProperty("sceneTarget").GetString().ShouldBe("Layout");
        action.GetProperty("targetLayout").GetGuid().ShouldBe(b);
    }

    [Fact]
    public async Task US2_1_A_SwitchWallScene_rule_with_target_Next_round_trips()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);

        string name = UniqueRuleName();
        HttpResponseMessage created = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(name, UniqueKind(), "$.payload.line == 3", wall, "Next", null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        JsonElement action = (await ReadRuleAsync(rules, name)).GetProperty("action");
        action.GetProperty("sceneTarget").GetString().ShouldBe("Next");
        action.GetProperty("targetLayout").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---- US2-2: matching event switches the wall, audited, broadcast ----------

    [Fact]
    public async Task US2_2_A_matching_event_switches_the_wall_and_is_audited_and_broadcast()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();

        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Layout", b);

        TaskCompletionSource<WallSceneChangedHubMessage> framed = new();
        await using HubConnection kiosk = await ListenForWallSceneChangedAsync(wall, framed);

        HttpResponseMessage ingested = await IngestManualAsync(events, kind, new { line = 3 });
        ingested.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(ingested));

        JsonElement? state = await WaitForShowingAsync(layouts, wall, b);
        state.ShouldNotBeNull("the wall never switched — the journey from ingest to the wall is broken");
        state.Value.GetProperty("sceneVersion").GetInt64().ShouldBe(1L);

        using CancellationTokenSource budget = new(EffectDeadline);
        WallSceneChangedHubMessage frame = await framed.Task.WaitAsync(budget.Token);
        frame.Showing.ShouldBe(b);
        frame.SceneVersion.ShouldBe(1L);

        (string Cause, Guid Rule) audited = await PollForWallSceneChangedAuditAsync(wall);
        audited.Cause.ShouldBe("Rule");
        audited.Rule.ShouldNotBe(Guid.Empty);

        (await CountAuditRowsAsync("WallSceneSwitchRequestedV1", wall)).ShouldBeGreaterThanOrEqualTo(
            1, "the rule-switch request itself must be audited (FR-008)");
    }

    // ---- US2-3: Next from a rule ------------------------------------------------

    [Fact]
    public async Task US2_3_Next_from_a_rule_advances_the_scene()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();

        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Next", null);
        (await IngestManualAsync(events, kind, new { line = 3 })).EnsureSuccessStatusCode();

        (await WaitForShowingAsync(layouts, wall, b)).ShouldNotBeNull();
    }

    // ---- US2-4 / US2-5: last writer wins (PD-1) --------------------------------

    [Fact]
    public async Task US2_4_Manual_then_rule_the_rule_wins()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();
        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Layout", b);

        (await IngestManualAsync(events, kind, new { line = 3 })).EnsureSuccessStatusCode();
        (await WaitForShowingAsync(layouts, wall, b)).ShouldNotBeNull();

        await SwitchAsync(layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = a });
        (await ReadWallAsync(layouts, wall)).GetProperty("showing").GetGuid().ShouldBe(a);

        (await IngestManualAsync(events, kind, new { line = 3 })).EnsureSuccessStatusCode();
        (await WaitForShowingAsync(layouts, wall, b)).ShouldNotBeNull(
            "the rule firing again must win back over the earlier manual switch (PD-1)");
    }

    [Fact]
    public async Task US2_5_Rule_then_manual_the_manual_switch_sticks_until_the_rule_fires_again()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();
        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Layout", b);

        (await IngestManualAsync(events, kind, new { line = 3 })).EnsureSuccessStatusCode();
        (await WaitForShowingAsync(layouts, wall, b)).ShouldNotBeNull();

        HttpResponseMessage manual = await SwitchAsync(
            layouts, wall, await WallVersionAsync(layouts, wall), new { target = "layout", layout = a });
        manual.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(manual));

        await Task.Delay(TimeSpan.FromSeconds(3));
        (await ReadWallAsync(layouts, wall)).GetProperty("showing").GetGuid().ShouldBe(
            a, "a manual switch must stay in place until the rule fires again (PD-1)");
    }

    // ---- US2-6: concurrent manual + rule switch — an invariant, not an ordering ----

    /// <summary>
    /// Does not assert which of the two writes commits first — PD-1 names no
    /// precedence — only that whichever ordering happened left the wall in a
    /// state consistent with its own audit trail (spec.md's third bullet).
    /// </summary>
    [Fact]
    public async Task US2_6_A_concurrent_manual_and_rule_switch_leave_the_wall_consistent()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();
        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Layout", b);

        int version = await WallVersionAsync(layouts, wall);

        Task<HttpResponseMessage> manual = SwitchAsync(layouts, wall, version, new { target = "layout", layout = b });
        Task<HttpResponseMessage> ingest = IngestManualAsync(events, kind, new { line = 3 });
        await Task.WhenAll(manual, ingest);

        // Let whichever ordering happened settle — the rule side is at least
        // one more async hop than the manual side even when it wins the race.
        JsonElement? settled = await WaitForShowingAsync(layouts, wall, b);
        settled.ShouldNotBeNull("neither write ever landed, so this case establishes nothing");

        JsonElement finalState = await ReadWallAsync(layouts, wall);
        long finalVersion = finalState.GetProperty("sceneVersion").GetInt64();
        Guid showing = finalState.GetProperty("showing").GetGuid();

        int changedCount = await CountAuditRowsAsync("WallSceneChangedV1", wall);
        changedCount.ShouldBe(
            (int)finalVersion,
            "the number of WallSceneChangedV1 rows must equal the sceneVersion delta in every ordering");

        Guid lastCurrentLayout = await LastWallSceneChangedCurrentLayoutAsync(wall);
        showing.ShouldBe(
            lastCurrentLayout,
            "Showing must equal the CurrentLayout of the last WallSceneChangedV1, whichever write committed first");
    }

    // ---- US2-7: bad request shapes ---------------------------------------------

    [Fact]
    public async Task US2_7_No_wallIdentifier_is_refused_with_400()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage refused = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(UniqueRuleName(), UniqueKind(), "$.payload.line == 3", null, "Layout", Guid.CreateVersion7()));
        await AssertRuleInvalidInputAsync(refused);
    }

    [Fact]
    public async Task US2_7_A_sceneTarget_that_is_not_Next_or_Layout_is_refused_with_400()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage refused = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(UniqueRuleName(), UniqueKind(), "$.payload.line == 3", Guid.CreateVersion7(), "Sideways", null));
        await AssertRuleInvalidInputAsync(refused);
    }

    [Fact]
    public async Task US2_7_Layout_with_no_targetLayoutIdentifier_is_refused_with_400()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage refused = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(UniqueRuleName(), UniqueKind(), "$.payload.line == 3", Guid.CreateVersion7(), "Layout", null));
        await AssertRuleInvalidInputAsync(refused);
    }

    [Fact]
    public async Task US2_7_Next_with_a_targetLayoutIdentifier_is_refused_with_400()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage refused = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(UniqueRuleName(), UniqueKind(), "$.payload.line == 3", Guid.CreateVersion7(), "Next", Guid.CreateVersion7()));
        await AssertRuleInvalidInputAsync(refused);
    }

    [Fact]
    public async Task US2_7_An_empty_guid_for_wall_is_refused_with_400()
    {
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        HttpResponseMessage refused = await rules.PostAsJsonAsync(
            "/rules", SwitchWallSceneRuleBody(UniqueRuleName(), UniqueKind(), "$.payload.line == 3", Guid.Empty, "Layout", Guid.CreateVersion7()));
        await AssertRuleInvalidInputAsync(refused);
    }

    private static async Task AssertRuleInvalidInputAsync(HttpResponseMessage refused)
    {
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()
            .ShouldBe("RULE_INVALID_INPUT");
    }

    // ---- US2-8: a rule in another fab cannot move the wall ---------------------

    /// <summary>
    /// Synced on a control effect (a <c>SetVariableValue</c> rule on the same
    /// event, same fab) rather than a bare sleep — mirrors
    /// <c>EventReachesItsEffectsTests.An_event_changes_only_the_variable_whose_rule_matches_it</c>.
    /// Once the control variable has moved, Automation's single fan-out pass
    /// over this event has already evaluated and published for the
    /// cross-fab rule too; polling for its audit row below additionally
    /// confirms LayoutComposition's side has had the request to refuse.
    /// </summary>
    [Fact]
    public async Task US2_8_A_rule_in_another_fab_cannot_move_the_wall()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAuthenticatedClientAsync(
            "automation", MultiFabOperator, OperatorPassword);
        using HttpClient variables = await aspire.CreateAuthenticatedClientAsync(
            "system-variables", MultiFabOperator, OperatorPassword);
        using HttpClient events = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", MultiFabOperator, OperatorPassword);

        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]); // munich

        string kind = UniqueKind();
        string control = $"ctrl{Guid.NewGuid():N}"[..16];
        await DefineVariableAsync(variables, control, "0");

        await ActivateSwitchWallSceneRuleAsync(
            rules, UniqueRuleName(), kind, wall, "Layout", b, fab: "dresden");
        await ActivateSetVariableRuleAsync(rules, UniqueRuleName(), kind, control, fab: "dresden");

        (await IngestManualAsync(events, kind, new { line = 3 }, fab: "dresden")).EnsureSuccessStatusCode();

        string? observed = await WaitForVariableAsync(variables, control, "1");
        observed.ShouldBe("1", "the control effect never arrived, so this case establishes nothing");

        // Give LayoutComposition's side the same chance to have processed
        // the (refused) request as the control effect just proved Automation
        // had to publish it.
        await PollForAuditCountAtLeastAsync("WallSceneSwitchRequestedV1", wall, atLeast: 1);
        await Task.Delay(TimeSpan.FromSeconds(2));

        (await ReadWallAsync(layouts, wall)).GetProperty("showing").GetGuid().ShouldBe(
            a, "a rule in another fab must not move this wall");
        (await CountAuditRowsAsync("WallSceneChangedV1", wall)).ShouldBe(
            0, "no WallSceneChangedV1 may be published for a cross-fab drop");
    }

    // ---- US2-11: auth ------------------------------------------------------------

    [Fact]
    public async Task US2_11_A_caller_without_sse_rules_write_is_refused_with_403()
    {
        (string ClientId, string ClientSecret) enrolled = await EnrollKioskAsync();
        string kioskToken = await ClientCredentialsTokenAsync(enrolled.ClientId, enrolled.ClientSecret);
        using HttpClient kiosk = aspire.CreateServiceClient("automation");
        kiosk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kioskToken);

        HttpResponseMessage refused = await kiosk.PostAsJsonAsync(
            "/rules",
            SwitchWallSceneRuleBody(
                UniqueRuleName(), UniqueKind(), "$.payload.line == 3", Guid.CreateVersion7(), "Next", null));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    // ---- US2-12 / US2-13: stale target drops ------------------------------------

    [Fact]
    public async Task US2_12_A_target_no_longer_in_the_wall_scenes_is_dropped()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid c = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        string kind = UniqueKind();

        // c is never a scene of this wall.
        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Layout", c);

        (await IngestManualAsync(events, kind, new { line = 3 })).EnsureSuccessStatusCode();
        await PollForAuditCountAtLeastAsync("WallSceneSwitchRequestedV1", wall, atLeast: 1);
        await Task.Delay(TimeSpan.FromSeconds(2));

        (await ReadWallAsync(layouts, wall)).GetProperty("showing").GetGuid().ShouldBe(a);
        (await CountAuditRowsAsync("WallSceneChangedV1", wall)).ShouldBe(0);
    }

    [Fact]
    public async Task US2_13_A_target_that_is_no_longer_Published_is_dropped()
    {
        using HttpClient layouts = await aspire.CreateAdminClientAsync("layout-composition");
        using HttpClient rules = await aspire.CreateAdminClientAsync("automation");
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        Guid a = await PublishedLayoutAsync(layouts);
        Guid b = await PublishedLayoutAsync(layouts);
        Guid wall = await CreateWallReturningIdAsync(layouts, [a, b]);
        await RevertToDraftAsync(layouts, b);
        string kind = UniqueKind();

        await ActivateSwitchWallSceneRuleAsync(rules, UniqueRuleName(), kind, wall, "Layout", b);

        (await IngestManualAsync(events, kind, new { line = 3 })).EnsureSuccessStatusCode();
        await PollForAuditCountAtLeastAsync("WallSceneSwitchRequestedV1", wall, atLeast: 1);
        await Task.Delay(TimeSpan.FromSeconds(2));

        (await ReadWallAsync(layouts, wall)).GetProperty("showing").GetGuid().ShouldBe(a);
        (await CountAuditRowsAsync("WallSceneChangedV1", wall)).ShouldBe(0);
    }

    // ---- arranging: layouts / walls (mirrors WallEndpointsTests) ---------------

    private async Task<Guid> PublishedLayoutAsync(HttpClient layouts, string fab = "munich")
    {
        string name = $"W296-{Guid.NewGuid():N}"[..16];
        Guid camera = await LayoutRequests.RegisterCameraAsync(aspire, fab);

        HttpResponseMessage created = await layouts.PostAsJsonAsync(
            "/layouts" + (fab == "munich" ? "" : $"?fabId={fab}"), new
            {
                name,
                grid = new { rows = 1, cols = 1 },
                tiles = new[]
                {
                    new { cameraIdentifier = camera, overlayIdentifier = (Guid?)null, row = 0, col = 0 },
                },
            });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage published = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/publish");
        published.EnsureSuccessStatusCode();

        return layoutIdentifier;
    }

    private static async Task RevertToDraftAsync(HttpClient layouts, Guid layoutIdentifier)
    {
        HttpResponseMessage reverted = await LayoutRequests.PostAsync(layouts, layoutIdentifier, "revisions/1/revert");
        reverted.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> CreateWallAsync(
        HttpClient layouts, string name, IReadOnlyList<Guid> scenes)
    {
        return layouts.PostAsJsonAsync("/walls", new { name, scenes });
    }

    private static async Task<Guid> CreateWallReturningIdAsync(HttpClient layouts, IReadOnlyList<Guid> scenes)
    {
        HttpResponseMessage created = await CreateWallAsync(layouts, UniqueWallName(), scenes);
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<int> WallVersionAsync(HttpClient layouts, Guid wall) =>
        (await ReadWallAsync(layouts, wall)).GetProperty("version").GetInt32();

    private static async Task<JsonElement> ReadWallAsync(HttpClient layouts, Guid wall)
    {
        HttpResponseMessage fetched = await layouts.GetAsync($"/walls/{wall}");
        fetched.EnsureSuccessStatusCode();
        return await fetched.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient layouts, Guid wall, int version, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/walls/{wall}/switch")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return layouts.SendAsync(request);
    }

    /// <summary>Polls <c>/walls/{wall}</c> until <c>showing</c> equals <paramref name="expected"/>.</summary>
    private static async Task<JsonElement?> WaitForShowingAsync(HttpClient layouts, Guid wall, Guid expected)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + EffectDeadline;
        while (DateTimeOffset.UtcNow < deadline)
        {
            JsonElement body = await ReadWallAsync(layouts, wall);
            if (body.GetProperty("showing").GetGuid() == expected)
            {
                return body;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return null;
    }

    private static string UniqueWallName() => $"Wall296-{Guid.NewGuid():N}"[..16];

    // ---- arranging: rules --------------------------------------------------------

    private static object SwitchWallSceneRuleBody(
        string name, string triggerKind, string predicate, Guid? wall, string sceneTarget, Guid? targetLayout) => new
    {
        name,
        triggerSource = "manual",
        triggerKind,
        predicate,
        actionType = "SwitchWallScene",
        variableName = (string?)null,
        valueExpression = (string?)null,
        overlayIdentifier = (Guid?)null,
        durationMs = (int?)null,
        wallIdentifier = wall,
        sceneTarget,
        targetLayoutIdentifier = targetLayout,
    };

    private static object SetVariableRuleBody(
        string name, string triggerKind, string predicate, string variable) => new
    {
        name,
        triggerSource = "manual",
        triggerKind,
        predicate,
        actionType = "SetVariableValue",
        variableName = variable,
        valueExpression = "1",
        overlayIdentifier = (Guid?)null,
        durationMs = (int?)null,
        wallIdentifier = (Guid?)null,
        sceneTarget = (string?)null,
        targetLayoutIdentifier = (Guid?)null,
    };

    private static async Task<string> ActivateSwitchWallSceneRuleAsync(
        HttpClient rules, string name, string triggerKind, Guid wall, string sceneTarget, Guid? targetLayout,
        string fab = "munich") =>
        await ActivateAsync(
            rules, name,
            SwitchWallSceneRuleBody(name, triggerKind, "$.payload.line == 3", wall, sceneTarget, targetLayout),
            fab);

    private static async Task<string> ActivateSetVariableRuleAsync(
        HttpClient rules, string name, string triggerKind, string variable, string fab = "munich") =>
        await ActivateAsync(
            rules, name, SetVariableRuleBody(name, triggerKind, "$.payload.line == 3", variable), fab);

    private static async Task<string> ActivateAsync(HttpClient rules, string name, object definition, string fab)
    {
        HttpResponseMessage created = await rules.PostAsJsonAsync($"/rules?fabId={fab}", definition);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        int version = await VersionOfAsync(rules, name, fab);

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish?fabId={fab}");
        publish.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        HttpResponseMessage published = await rules.SendAsync(publish);
        published.EnsureSuccessStatusCode();

        JsonElement body = await ReadRuleAsync(rules, name, fab);
        body.GetProperty("state").GetString().ShouldBe("Active");

        return name;
    }

    private static async Task<int> VersionOfAsync(HttpClient rules, string name, string fab) =>
        (await ReadRuleAsync(rules, name, fab)).GetProperty("version").GetInt32();

    private static async Task<JsonElement> ReadRuleAsync(HttpClient rules, string name, string fab = "munich")
    {
        HttpResponseMessage fetched = await rules.GetAsync($"/rules/{name}?fabId={fab}");
        fetched.EnsureSuccessStatusCode();
        return await fetched.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string UniqueRuleName() => $"switch{Guid.NewGuid():N}"[..18];

    private static string UniqueKind() => $"LineStop{Guid.NewGuid():N}"[..24];

    // ---- arranging: event ingestion ----------------------------------------------

    private static Task<HttpResponseMessage> IngestManualAsync(
        HttpClient events, string kind, object payload, string? fab = null)
    {
        string path = fab is null ? "/events/manual" : $"/events/manual?fabId={fab}";
        return events.PostAsJsonAsync(path, new
        {
            deviceId = "spec296-device",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload,
        });
    }

    // ---- arranging: system variables (US2-8's control effect) --------------------

    private static async Task DefineVariableAsync(HttpClient variables, string name, string initial)
    {
        HttpResponseMessage defined = await variables.PostAsJsonAsync("/system-variables?fabId=dresden", new
        {
            name,
            type = "Number",
            initialValue = initial,
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        defined.EnsureSuccessStatusCode();
    }

    private static async Task<string?> WaitForVariableAsync(HttpClient variables, string name, string expected)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + EffectDeadline;
        string? observed = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            observed = await ReadVariableAsync(variables, name);
            if (observed == expected)
            {
                return observed;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return observed;
    }

    private static async Task<string?> ReadVariableAsync(HttpClient variables, string name)
    {
        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}?fabId=dresden");
        if (!fetched.IsSuccessStatusCode)
        {
            return null;
        }

        JsonElement body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("value", out JsonElement value) ? value.GetString() : null;
    }

    // ---- arranging: auth (mirrors WallEndpointsTests' kiosk helpers) -------------

    private async Task<(string ClientId, string ClientSecret)> EnrollKioskAsync()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = $"t296-rule-{Guid.CreateVersion7():N}";

        HttpResponseMessage enrolled = await identity.PostAsJsonAsync(
            "/kiosks/enroll?fabId=munich", new { clientId });
        enrolled.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(enrolled));

        JsonElement body = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }

    private async Task<string> ClientCredentialsTokenAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage token = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        token.EnsureSuccessStatusCode();

        return (await token.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    // ---- observing: the hub -------------------------------------------------------

    private async Task<HubConnection> ListenForWallSceneChangedAsync(
        Guid wall, TaskCompletionSource<WallSceneChangedHubMessage> changed)
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        HubConnection kiosk = new HubConnectionBuilder()
            .WithUrl(
                aspire.HubUri("layout-composition", LayoutLifecycleHub.Path),
                options => options.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .Build();

        kiosk.On<JsonElement>(
            nameof(ILayoutLifecycleClient.WallSceneChanged),
            frame =>
            {
                if (frame.GetProperty("wall").GetGuid() == wall)
                {
                    changed.TrySetResult(new WallSceneChangedHubMessage(
                        wall,
                        frame.GetProperty("showing").GetGuid(),
                        frame.GetProperty("sceneVersion").GetInt64()));
                }
            });

        await kiosk.StartAsync();
        return kiosk;
    }

    // ---- observing: audit ----------------------------------------------------------

    private async Task<(string Cause, Guid Rule)> PollForWallSceneChangedAuditAsync(Guid wall)
    {
        const string sql = """
            SELECT (payload->>'Cause') || '|' || (payload->>'Rule') AS "Value"
            FROM audit_events
            WHERE event_kind = {0}
              AND payload->>'Wall' = {1}
            ORDER BY occurred_at DESC
            """;

        for (int attempt = 0; attempt < 120; attempt++)
        {
            await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();
            List<string> rows = await context.Database
                .SqlQueryRaw<string>(sql, "WallSceneChangedV1", wall.ToString())
                .ToListAsync();

            if (rows.Count > 0)
            {
                string[] parts = rows[0].Split('|');
                return (parts[0], Guid.Parse(parts[1]));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No WallSceneChangedV1 audit row for wall {wall} within 60s.");
    }

    private async Task<Guid> LastWallSceneChangedCurrentLayoutAsync(Guid wall)
    {
        const string sql = """
            SELECT payload->>'CurrentLayout' AS "Value"
            FROM audit_events
            WHERE event_kind = {0}
              AND payload->>'Wall' = {1}
            ORDER BY occurred_at DESC
            """;

        await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();
        List<string> rows = await context.Database
            .SqlQueryRaw<string>(sql, "WallSceneChangedV1", wall.ToString())
            .ToListAsync();

        return Guid.Parse(rows[0]);
    }

    private async Task<int> CountAuditRowsAsync(string eventKind, Guid wall)
    {
        await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> rows = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = {eventKind}
                  AND payload->>'Wall' = {wall.ToString()}
                """)
            .ToListAsync();

        return rows[0];
    }

    private async Task PollForAuditCountAtLeastAsync(string eventKind, Guid wall, int atLeast)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            if (await CountAuditRowsAsync(eventKind, wall) >= atLeast)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"{eventKind} audit row for wall {wall} never reached count {atLeast} within 60s.");
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}";
}
