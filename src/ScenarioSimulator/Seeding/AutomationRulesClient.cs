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

        using HttpRequestMessage create = new(HttpMethod.Post, "/rules")
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

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();

        logger.RuleSeeded(name, overlay);
    }

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
