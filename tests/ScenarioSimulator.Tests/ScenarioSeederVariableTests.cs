using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-C, T-C04. <c>AssetDefinition.Variables</c> and
/// <c>SystemVariablesClient</c> now exist, and <c>ScenarioSeeder</c> seeds
/// variables before the overlay and the reactions (plan.md §3.3, §3.4, §5.4,
/// T-C05) — drives the real, unmodified <c>ScenarioSeeder</c> the same way
/// <c>ScenarioSeederReactionTests</c> does, so the pins here come from the
/// actual seeding path.
///
/// <para>
/// <c>ScenarioSeeder</c>'s constructor takes <c>SystemVariablesClient
/// variables</c> as its fourth parameter, right after <c>AutomationRulesClient
/// rules</c> (before <c>AssetCorrelationTable correlation</c>) — the
/// seeding-dependency order the constructor already lists in (catalog,
/// overlays, rules, variables, ...).
/// </para>
///
/// <para>
/// <see cref="ScenarioSeederReactionTests"/> and
/// <see cref="LegacyHighlightRuleBodyTests"/> must keep passing
/// <b>unmodified</b> once this PR lands (FR-009, SC-003) — that is checked by
/// re-running those files, not by duplicating them here.
/// </para>
/// </summary>
public sealed class ScenarioSeederVariableTests
{
    private static readonly Guid Overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    /// <summary>
    /// Plan.md §5.4's seeding order: Variables → Overlay → legacy Highlight →
    /// Reactions → Camera. Ordering is observed across three independent
    /// handlers via a shared, tagged call log — the only way to see the
    /// relative order of calls that land on different HTTP clients.
    /// </summary>
    [Fact]
    public async Task Variables_are_seeded_before_the_overlay_and_the_reactions()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Variables = [new VariableDefinition { Name = "roughing_zone_state", Type = "String" }];
        asset.Highlight = new HighlightDefinition
        {
            TriggerKind = "Temperature",
            Comparison = "gte",
            Threshold = 1_100,
            DurationMs = 4_000,
        };
        asset.Sensors = [new SensorDefinition { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 }];
        asset.Reactions =
        [
            new ReactionDefinition
            {
                Name = "person-in-exclusion",
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
            },
        ];

        List<string> order = [];
        RecordingVariablesHandler variablesHandler = new();
        RecordingAutomationHandler automation = new();

        await RunSeederAsync(
            OneAssetScenario(asset), automation, variablesHandler, order,
            clipsDirectory: string.Empty, expectedVariableCreates: 1, expectedRuleCreates: 2);

        int firstVariablesCall = order.IndexOf("variables");
        int firstOverlayCall = order.IndexOf("overlay");
        int firstRulesCall = order.IndexOf("rules");

        firstVariablesCall.ShouldBeGreaterThanOrEqualTo(0, "the variables handler was never called");
        firstOverlayCall.ShouldBeGreaterThanOrEqualTo(0, "the overlay handler was never called");
        firstRulesCall.ShouldBeGreaterThanOrEqualTo(0, "the rules handler was never called");
        firstVariablesCall.ShouldBeLessThan(firstOverlayCall, "variables must seed before the overlay");
        firstOverlayCall.ShouldBeLessThan(firstRulesCall, "the overlay must seed before any rule (legacy Highlight or Reactions)");
    }

    /// <summary>
    /// Only the reaction that writes the refused variable is skipped; a
    /// reaction with no dependency on it still seeds (not "all reactions on
    /// that asset" — the same distinction T-B08 draws for a refused manifest).
    /// </summary>
    [Fact]
    public async Task A_refused_variable_skips_exactly_the_reactions_that_write_it()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Variables = [new VariableDefinition { Name = "roughing_zone_state", Type = "String" }];
        asset.Reactions =
        [
            new ReactionDefinition // writes the refused variable — must be skipped
            {
                Name = "zone-state",
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                Then = new ReactionAction { Type = "SetVariableValue", Variable = "roughing_zone_state", Value = "$.payload.label" },
            },
            new ReactionDefinition // unrelated to the refused variable — must still seed
            {
                Name = "person-in-exclusion",
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
            },
        ];

        // "roughing_zone_state" already exists server-side as Number: the
        // asset's declared String conflicts, so EnsureVariableAsync refuses it.
        RecordingVariablesHandler variablesHandler = new(
            preExisting: new Dictionary<string, string> { ["roughing_zone_state"] = "Number" });
        RecordingAutomationHandler automation = new();
        RecordingLogger<ScenarioSeeder> log = new();

        await RunSeederAsync(
            OneAssetScenario(asset), automation, variablesHandler, order: [],
            clipsDirectory: string.Empty, expectedVariableCreates: 1, expectedRuleCreates: 1, log);

        automation.Creates.Count.ShouldBe(1);
        automation.Creates[0].Body.Name.ShouldBe("rolling-mill-station-4-roughing-person-in-exclusion");

        log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("roughing_zone_state", StringComparison.Ordinal)
            && entry.Message.Contains("zone-state", StringComparison.Ordinal));
    }

    /// <summary>
    /// S4 (backend-reviewer, spec 289 PR-C). The end-to-end "variable seeds,
    /// then its dependent <c>SetVariableValue</c> reaction's rule is created"
    /// sequence that <c>ReactionRuleSeedTests.cs</c>'s doc comment (T-C03) has
    /// always claimed this class covers — until this test, nothing here
    /// actually drove a <c>SetVariableValue</c> reaction to a successful rule
    /// create; <see cref="A_refused_variable_skips_exactly_the_reactions_that_write_it"/>
    /// only exercises the refused path, where the reaction is skipped rather
    /// than seeded.
    /// </summary>
    [Fact]
    public async Task A_successfully_seeded_variable_lets_its_SetVariableValue_reaction_create_the_rule_with_the_expected_wire_body()
    {
        AssetDefinition asset = Asset("electronics-inspection", "inspection.mp4");
        asset.Variables = [new VariableDefinition { Name = "inspection_last_defect", Type = "String" }];
        asset.Reactions =
        [
            new ReactionDefinition
            {
                Name = "defect-detected",
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'defect'" },
                Then = new ReactionAction { Type = "SetVariableValue", Variable = "inspection_last_defect", Value = "$.payload.label" },
            },
        ];

        RecordingVariablesHandler variablesHandler = new();
        RecordingAutomationHandler automation = new();

        await RunSeederAsync(
            OneAssetScenario(asset), automation, variablesHandler, order: [],
            clipsDirectory: string.Empty, expectedVariableCreates: 1, expectedRuleCreates: 1);

        variablesHandler.Creates.ShouldHaveSingleItem();
        variablesHandler.Creates[0].Body.Name.ShouldBe("inspection_last_defect");

        CreateCall create = automation.Creates.ShouldHaveSingleItem();
        create.PathAndQuery.ShouldBe("/rules?fabId=munich");
        create.Body.ShouldBe(new RuleBody(
            "rolling-mill-electronics-inspection-defect-detected",
            "inference", "ObjectDetected",
            "$.device == 'electronics-inspection' && ($.payload.class == 'defect')",
            "SetVariableValue", "inspection_last_defect", "$.payload.label", null, null));

        automation.Publishes.ShouldContain(
            call => call.PathAndQuery == "/rules/rolling-mill-electronics-inspection-defect-detected/publish?fabId=munich");
    }

    /// <summary>
    /// Variable names are fab-global (plan.md §3.3): two assets declaring the
    /// same name with the same type is fine. Each asset still attempts its own
    /// define — the idempotent create-then-resolve pattern every seeding
    /// client here already follows — but the server records exactly one row,
    /// and the second attempt's 409 is resolved by a read-back, not treated as
    /// a duplicate create.
    /// </summary>
    [Fact]
    public async Task The_same_variable_declared_by_two_assets_with_the_same_type_seeds_once_and_the_second_reuses_it()
    {
        AssetDefinition assetA = Asset("station-4-roughing", "mill-roughing.mp4");
        assetA.Variables = [new VariableDefinition { Name = "shared_state", Type = "String" }];

        AssetDefinition assetB = Asset("station-7-cutting", "sim-loop.mp4");
        assetB.Variables = [new VariableDefinition { Name = "shared_state", Type = "String" }];

        RecordingVariablesHandler variablesHandler = new();
        RecordingAutomationHandler automation = new();

        await RunSeederAsync(
            TwoAssetScenario(assetA, assetB), automation, variablesHandler, order: [],
            clipsDirectory: string.Empty, expectedVariableCreates: 2, expectedRuleCreates: 0);

        variablesHandler.Creates.Count.ShouldBe(2, "both assets attempt the define — idempotency is a server-side property, not a client-side skip");
        variablesHandler.Creates.ShouldAllBe(call => call.Body.Name == "shared_state");
        variablesHandler.Reads.ShouldBe(["shared_state"], "only the second attempt's 409 needed a read-back");
    }

    private static ScenarioOptions OneAssetScenario(AssetDefinition asset) => new()
    {
        Active = ["rolling-mill"],
        Scenarios = new Dictionary<string, ScenarioDefinition>
        {
            ["rolling-mill"] = new() { Name = "Rolling Mill", Assets = [asset] },
        },
    };

    private static ScenarioOptions TwoAssetScenario(AssetDefinition assetA, AssetDefinition assetB) => new()
    {
        Active = ["rolling-mill"],
        Scenarios = new Dictionary<string, ScenarioDefinition>
        {
            ["rolling-mill"] = new() { Name = "Rolling Mill", Assets = [assetA, assetB] },
        },
    };

    private static AssetDefinition Asset(string key, string clip) => new()
    {
        Key = key,
        Name = key,
        Camera = new CameraDefinition { Path = key, Clip = clip },
        Overlay = new OverlayDefinition { Label = key, X = 0.1, Y = 0.05, Width = 0.8, Height = 0.18, FontSize = 24 },
        Tile = new TileDefinition { Row = 0, Col = 0 },
    };

    private static async Task RunSeederAsync(
        ScenarioOptions options,
        RecordingAutomationHandler automation,
        RecordingVariablesHandler variablesHandler,
        List<string> order,
        string clipsDirectory,
        int expectedVariableCreates,
        int expectedRuleCreates,
        ILogger<ScenarioSeeder>? log = null)
    {
        options.ClipsDirectory = clipsDirectory;
        IOptions<ScenarioOptions> wrapped = Options.Create(options);
        AssetCorrelationTable correlation = new();

        HttpClient variablesClient = new(new OrderTrackingHandler("variables", order, variablesHandler))
        {
            BaseAddress = new Uri("https://system-variables.test"),
        };
        HttpClient overlayClient = new(new OrderTrackingHandler("overlay", order, new OverlayHandler(Overlay)))
        {
            BaseAddress = new Uri("https://overlays.test"),
        };
        HttpClient rulesClient = new(new OrderTrackingHandler("rules", order, automation))
        {
            BaseAddress = new Uri("https://automation.test"),
        };

        ScenarioSeeder seeder = new(
            new CameraCatalogClient(Failing(), Tokens(), Simulator(), NullLogger<CameraCatalogClient>.Instance),
            new OverlayDesignerClient(overlayClient, Tokens(), NullLogger<OverlayDesignerClient>.Instance),
            new AutomationRulesClient(rulesClient, Tokens(), NullLogger<AutomationRulesClient>.Instance),
            new SystemVariablesClient(variablesClient, Tokens(), NullLogger<SystemVariablesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            log ?? NullLogger<ScenarioSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);
        await WaitForAsync(() =>
            variablesHandler.Creates.Count >= expectedVariableCreates
            && automation.Creates.Count >= expectedRuleCreates);
        await seeder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
            {
                // One more beat so a refused seed's warning (which produces no
                // create) has had time to be logged too.
                await Task.Delay(50);
                return;
            }

            await Task.Delay(50);
        }
    }

    private static HttpClient Failing() =>
        new(new ThrowingHandler()) { BaseAddress = new Uri("https://unreachable.test") };

    private static IOptions<SimulatorOptions> Simulator() =>
        Options.Create(new SimulatorOptions
        {
            KeycloakUrl = "https://keycloak.test",
            Realm = "smart-sentinel-eye",
            ClientId = "scenario-simulator",
            ClientSecret = "stub-secret",
        });

    private static KeycloakTokenProvider Tokens() =>
        new(new FakeHttpClientFactory(new HttpClient(new StubTokenHandler())), Simulator(), TimeProvider.System, NullLogger<KeycloakTokenProvider>.Instance);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record VariableBody(string Name, string Type, string? InitialValue, string? TruthyLabel, string? FalsyLabel);

    private sealed record VariableCreateCall(VariableBody Body);

    private sealed record RuleBody(
        string Name, string TriggerSource, string TriggerKind, string Predicate, string ActionType,
        string? VariableName, string? ValueExpression, Guid? OverlayIdentifier, int? DurationMs);

    private sealed record CreateCall(string PathAndQuery, RuleBody Body);

    private sealed record PublishCall(string PathAndQuery, string IfMatch);

    /// <summary>Wraps another handler and appends <paramref name="tag"/> to the shared, ordered call log before delegating.</summary>
    private sealed class OrderTrackingHandler(string tag, List<string> order, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (order)
            {
                order.Add(tag);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Simulates the SystemVariables API's idempotent define: the first
    /// <c>POST</c> for a name (not already known) creates it (201); any later
    /// one for the same name conflicts (409), resolved by a
    /// <c>GET /system-variables/{name}</c> read-back. <paramref name="preExisting"/>
    /// seeds names as if a previous run (or another fab caller) already
    /// defined them, so the very first <c>POST</c> for that name conflicts too
    /// — the shape T-C04's mismatched-type refusal needs.
    /// </summary>
    private sealed class RecordingVariablesHandler(IReadOnlyDictionary<string, string>? preExisting = null) : HttpMessageHandler
    {
        private readonly Dictionary<string, string> known =
            preExisting is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(preExisting, StringComparer.Ordinal);

        public List<VariableCreateCall> Creates { get; } = [];

        public List<string> Reads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path == "/system-variables")
            {
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                VariableBody body = JsonSerializer.Deserialize<VariableBody>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Variable create body did not deserialize.");
                lock (Creates)
                {
                    Creates.Add(new VariableCreateCall(body));
                }

                lock (known)
                {
                    if (known.ContainsKey(body.Name))
                    {
                        return new HttpResponseMessage(HttpStatusCode.Conflict);
                    }

                    known[body.Name] = body.Type;
                    return new HttpResponseMessage(HttpStatusCode.Created);
                }
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/system-variables/", StringComparison.Ordinal))
            {
                string name = path["/system-variables/".Length..];
                lock (Reads)
                {
                    Reads.Add(name);
                }

                string type;
                lock (known)
                {
                    type = known.TryGetValue(name, out string? existing) ? existing : "Number";
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"type":"{{type}}"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class RecordingAutomationHandler : HttpMessageHandler
    {
        public List<CreateCall> Creates { get; } = [];

        public List<PublishCall> Publishes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string pathAndQuery = request.RequestUri!.PathAndQuery;

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/rules")
            {
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                RuleBody body = JsonSerializer.Deserialize<RuleBody>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Rule create body did not deserialize.");
                lock (Creates)
                {
                    Creates.Add(new CreateCall(pathAndQuery, body));
                }

                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/publish", StringComparison.Ordinal))
            {
                string ifMatch = request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values) : string.Empty;
                lock (Publishes)
                {
                    Publishes.Add(new PublishCall(pathAndQuery, ifMatch));
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class OverlayHandler(Guid overlay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/overlays")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"\"{overlay}\"", Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("service did not answer in time"));
    }

    private sealed class StubTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""",
                    Encoding.UTF8, "application/json"),
            });
    }
}
