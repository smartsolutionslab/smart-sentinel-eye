using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>POST /rules</c> answers, in which order it refuses, which of two invalid fields
/// wins, what it hands the idempotency store and what a replay returns (spec 367, #2932,
/// FR-003), so that splitting <c>Create</c> under ADR-0084's thirty lines cannot move the key
/// read, the fab resolution or the body parse relative to one another, nor reorder the five field
/// parses.
///
/// <para>
/// The real create handler runs; only the rule repository, the fab guard and the idempotency
/// store behind it are doubles. Problem bodies are compared as the exact JSON. A caller the fab
/// guard refuses surfaces as <see cref="FabAuthorizationException"/> thrown out of the delegate:
/// the global exception handler maps it to 403 in the running service, and an in-process drive
/// does not include that handler, so the throw is what is pinned.
/// </para>
///
/// <para>
/// The four parse-order pairs each make two fields invalid at once and assert that the earlier
/// one's message is the one the caller receives, so a swap of any adjacent pair in the parse
/// changes a message and fails. The messages are the value objects' own and
/// <c>RulesEndpoints.BuildAction</c>'s (the latter also pinned by
/// <c>RuleActionBuildCharacterisationTests</c>).
/// </para>
/// </summary>
public class RuleCreateEndpointCharacterisationTests
{
    private const string Create = "/rules/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string FabRequired =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"RULE_FAB_REQUIRED","status":400,"detail":"You are assigned to more than one fab; name the one this applies to with ?fabId=."}""";

    private const string ValidPredicate = "$.payload.cycleTime <= 30";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid Resource = new("0190a0f0-0000-7000-8000-0000000000aa");

    private static readonly string[] TwoFabs = ["dresden", "bremen"];

    [Fact]
    public async Task Create_reads_a_malformed_idempotency_key_before_it_resolves_the_fab()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller(TwoFabs), Valid(), key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_by_a_multi_fab_caller_who_names_no_fab_is_refused_before_the_store_is_asked()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller(TwoFabs), Valid(), key: "key-1");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(FabRequired);
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_resolves_the_fab_before_it_parses_the_body()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller(TwoFabs), Body(name: "Not A Name", predicate: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(FabRequired);
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_lets_the_fab_guards_refusal_escape_before_the_body_is_parsed_or_the_store_asked()
    {
        await using var host = new AutomationEndpointHost();
        host.FabGuard
            .Setup(guard => guard.EnsureAccessAsync(
                It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FabAuthorizationException("bremen"));

        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            CreateAsync(host, Caller("dresden"), Body(name: "Not A Name"), key: "key-1", fabId: "bremen"));

        refusal.FabId.ShouldBe("bremen");
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_names_the_name_when_the_name_and_the_predicate_are_both_invalid()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Body(name: "Not A Name", predicate: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(NameRefusal));
        host.Rules.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_names_the_predicate_when_the_predicate_and_the_action_are_both_invalid()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Body(predicate: " ", actionType: "Explode"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(PredicateRefusal));
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_names_the_action_when_the_action_and_the_trigger_source_are_both_invalid()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Body(variableName: null, triggerSource: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("VariableName is required for SetVariableValue actions."));
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_names_the_trigger_source_when_the_trigger_source_and_the_trigger_kind_are_both_invalid()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Body(triggerSource: new string('s', 17), triggerKind: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(SourceRefusal));
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_names_the_trigger_kind_when_it_is_the_only_invalid_field()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Body(triggerKind: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(KindRefusal));
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_refuses_an_unknown_action_type_with_the_expected_list()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Body(actionType: "Explode"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(
            "Unknown ActionType 'Explode'. Expected: SetVariableValue | HighlightOverlay | SwitchWallScene."));
        host.Rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_create_reserves_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new AutomationEndpointHost();
        CreateRuleRequest body = new(
            "line-alarm", "plc", "PlcCycleStart", ValidPredicate, "SetVariableValue", "oeeLine1", "100", null, null, null, null, null);

        var response = await CreateAsync(host, Caller("dresden"), Valid(), key: "key-1");

        response.Status.ShouldBe(201);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /rules");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                Added(host).Id.Value,
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_unkeyed_create_answers_201_with_the_rule_name_as_location_and_the_identifier_as_body()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller("dresden"), Valid());

        var added = Added(host);
        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/rules/line-alarm");
        response.Body.ShouldBe($"\"{added.Id.Value}\"");
        added.Fab.Value.ShouldBe("dresden");
        added.Name.Value.ShouldBe("line-alarm");
        added.TriggerSource.Value.ShouldBe("plc");
        added.TriggerKind.Value.ShouldBe("PlcCycleStart");
        added.Predicate.Value.ShouldBe(ValidPredicate);
        var action = added.Action.ShouldBeOfType<RuleAction.SetVariableValue>();
        action.VariableName.ShouldBe("oeeLine1");
        action.ValueExpression.ShouldBe("100");
        added.State.ShouldBe(RuleState.Draft);
        added.Creation.By.Value.ShouldBe(Subject);
        host.Rules.Verify(
            rules => rules.GetByNameAsync(
                FabIdentifier.From("dresden"), RuleName.From("line-alarm"), It.IsAny<CancellationToken>()),
            Times.Once);
        host.Rules.Verify(rules => rules.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_rule_is_created_in_and_is_checked_with_the_guard()
    {
        await using var host = new AutomationEndpointHost();

        var response = await CreateAsync(host, Caller(TwoFabs), Valid(), fabId: "bremen");

        response.Status.ShouldBe(201);
        Added(host).Fab.Value.ShouldBe("bremen");
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(
                It.Is<ClaimsPrincipal>(user => user.FindFirst("sub")!.Value == Subject.ToString()),
                "bremen",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_keyed_create_the_handler_refuses_answers_the_handlers_problem_and_releases_the_key()
    {
        await using var host = new AutomationEndpointHost();
        var existing = Rule.Create(
            FabIdentifier.From("dresden"),
            RuleName.From("line-alarm"),
            TriggerSource.From("plc"),
            TriggerKind.From("PlcCycleStart"),
            RulePredicate.From(ValidPredicate),
            RuleAction.SetVariableValue.From("oeeLine1", "100"),
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Rules
            .Setup(rules => rules.GetByNameAsync(
                FabIdentifier.From("dresden"), RuleName.From("line-alarm"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Rule>.Some(existing));

        var response = await CreateAsync(host, Caller("dresden"), Valid(), key: "key-1");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(AutomationEndpointHost.Problem(
            409,
            "RULE_NAME_TAKEN",
            "A non-archived rule named 'line-alarm' already exists in fab 'dresden'.",
            ConflictType));
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Rules.Verify(rules => rules.Add(It.IsAny<Rule>()), Times.Never);
    }

    [Fact]
    public async Task A_replayed_create_answers_201_with_the_stored_identifier_and_never_reaches_the_repository()
    {
        await using var host = new AutomationEndpointHost();
        host.StoreReplaysAs(Resource);

        var response = await CreateAsync(host, Caller("dresden"), Valid(), key: "key-1");

        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/rules/line-alarm");
        response.Body.ShouldBe($"\"{Resource}\"");
        host.Rules.VerifyNoOtherCalls();
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static string NameRefusal { get; } =
        "value: must start with a lowercase letter and contain only lowercase letters, digits, or '-' (Parameter 'value')";

    private static string PredicateRefusal { get; } = "value must not be null or whitespace. (Parameter 'value')";

    private static string SourceRefusal { get; } = "value must be no more than 16 character(s). (Parameter 'value')";

    private static string KindRefusal { get; } = "value must not be null or whitespace. (Parameter 'value')";

    private static ClaimsPrincipal Caller(params string[] fabs) => AutomationEndpointHost.Operator(Subject, fabs);

    private static string InvalidInput(string detail) =>
        AutomationEndpointHost.Problem(400, "RULE_INVALID_INPUT", detail, BadRequestType);

    private static Rule Added(AutomationEndpointHost host) =>
        host.Rules.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IRuleRepository.Add))
            .SelectMany(invocation => invocation.Arguments.OfType<Rule>())
            .ShouldHaveSingleItem();

    private static string Valid() => Body();

    private static string Body(
        string name = "line-alarm",
        string triggerSource = "plc",
        string triggerKind = "PlcCycleStart",
        string predicate = ValidPredicate,
        string actionType = "SetVariableValue",
        string? variableName = "oeeLine1")
    {
        return JsonSerializer.Serialize(new
        {
            name,
            triggerSource,
            triggerKind,
            predicate,
            actionType,
            variableName,
            valueExpression = "100",
            overlayIdentifier = (Guid?)null,
            durationMs = (int?)null,
            wallIdentifier = (Guid?)null,
            sceneTarget = (string?)null,
            targetLayoutIdentifier = (Guid?)null,
        });
    }

    private static Task<AutomationEndpointHost.HostResponse> CreateAsync(
        AutomationEndpointHost host,
        ClaimsPrincipal user,
        string body,
        string? key = null,
        string? fabId = null) =>
        host.SendAsync(
            "POST",
            Create,
            user,
            bodyJson: body,
            headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key },
            query: fabId is null ? null : "?fabId=" + fabId);
}
