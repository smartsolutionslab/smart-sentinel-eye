using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Seeds + publishes a per-asset HighlightOverlay rule over the Automation REST
/// API (ADR-0111 M2). The AEL predicate keys the highlight to one device + one
/// threshold so it lands on exactly that station's tile. Idempotent: a duplicate
/// name (409) is treated as already seeded (rules key on name). Bearer via the
/// scenario-simulator grant (scope sse.rules.write).
/// </summary>
public sealed class AutomationRulesClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    ILogger<AutomationRulesClient> logger)
{
    public async Task EnsureRuleAsync(
        string name,
        string triggerSource,
        string triggerKind,
        string device,
        string comparison,
        double threshold,
        Guid overlay,
        int durationMs,
        CancellationToken cancellationToken)
    {
        string predicate =
            $"$.device == '{device}' && $.payload.value {Operator(comparison)} {threshold.ToString(CultureInfo.InvariantCulture)}";

        string token = await tokens.GetAccessTokenAsync(cancellationToken);

        // fabId is explicit rather than inferred: the simulator's service
        // account holds no fab group, so there is nothing to infer from
        // (spec 013, ADR-0114). Matches MqttSampleMapper.FabId, which is what
        // the events this rule reacts to are stamped with.
        using HttpRequestMessage create = new(HttpMethod.Post, $"/rules?fabId={FabId}")
        {
            Content = JsonContent.Create(
                new CreateRuleBody(name, triggerSource, triggerKind, predicate, "HighlightOverlay", null, null, overlay, durationMs)),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage created = await http.SendAsync(create, cancellationToken);

        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            logger.RuleAlreadyExists(name);
            return;
        }

        created.EnsureSuccessStatusCode();

        // A freshly created rule is at version 0 — the interceptor does not
        // bump Added roots — so that is the precondition to echo back. Without
        // it publish returns 428; spec 012 made the header mandatory and this
        // caller was never updated, so rule seeding has been failing since.
        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish?fabId={FabId}");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        publish.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();

        logger.RuleSeeded(name, overlay);
    }

    /// <summary>
    /// The fab the simulator seeds into. Mirrors <c>MqttSampleMapper.FabId</c>
    /// — a rule must live in the same fab as the events it reacts to, or it
    /// will never fire (spec 013).
    /// </summary>
    private const string FabId = "munich";

    private static string Operator(string comparison) => (comparison ?? string.Empty).ToLowerInvariant() switch
    {
        "gte" => ">=",
        "lte" => "<=",
        "gt" => ">",
        "lt" => "<",
        "eq" => "==",
        "ne" => "!=",
        _ => ">=",
    };

    private sealed record CreateRuleBody(
        string Name,
        string TriggerSource,
        string TriggerKind,
        string Predicate,
        string ActionType,
        string VariableName,
        string ValueExpression,
        Guid? OverlayIdentifier,
        int? DurationMs);
}
