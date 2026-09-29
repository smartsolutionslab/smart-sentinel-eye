using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Seeds + publishes a per-asset HighlightOverlay rule over the Automation REST
/// API (ADR-0111 M2). The AEL predicate keys the highlight to one device + one
/// threshold so it lands on exactly that station's tile. Idempotent: names key
/// on (fab, name), and a rule that already exists is published only if it is
/// still Draft. Bearer via the scenario-simulator grant (scopes
/// sse.rules.write + sse.rules.read).
/// </summary>
public sealed class AutomationRulesClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    ILogger<AutomationRulesClient> logger)
{
    internal async Task EnsureRuleAsync(RuleSeed seed, CancellationToken cancellationToken)
    {
        Ensure.That(seed).IsNotNull();

        (string name, string triggerSource, string triggerKind, string predicate, RuleSeedAction action) = seed;

        if (action is not RuleSeedAction.HighlightOverlay highlight)
        {
            throw new NotSupportedException($"Unsupported rule seed action '{action.GetType().Name}'.");
        }

        CreateRuleBody body = new(
            name, triggerSource, triggerKind, predicate, "HighlightOverlay", null!, null!, highlight.Overlay, highlight.DurationMs);

        string token = await tokens.GetAccessTokenAsync(cancellationToken);

        int? expectedVersion = await CreateOrResolveVersionAsync(name, body, token, cancellationToken);
        if (expectedVersion is null)
        {
            return;
        }

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/rules/{name}/publish?fabId={FabId}");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        publish.Headers.TryAddWithoutValidation(
            "If-Match", ConcurrencyHeaders.ETag(expectedVersion.Value));
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();

        logger.RuleSeeded(name, highlight.Overlay);
    }

    /// <summary>
    /// Creates the rule and returns the version to publish against — 0 for a
    /// freshly created rule (the interceptor does not bump Added roots), or the
    /// stored version when a 409 finds it already there. <c>null</c> means the
    /// caller has nothing left to do: the existing rule is no longer a Draft, so
    /// publishing it would be wrong.
    /// <para>
    /// fabId is explicit rather than inferred (ADR-0114): the rule has to land
    /// in the fab whose events it reacts to — <c>MqttSampleMapper.FabId</c> —
    /// which is not the same question as which fab the service account happens
    /// to be assigned to.
    /// </para>
    /// <para>
    /// On 409 the rule is already there but its state is unknown: every run
    /// between spec 012 making If-Match mandatory and this client learning to
    /// send it created the rule and then failed to publish it, so an existing
    /// database can hold a Draft that will never fire. Ask what state it is in
    /// rather than assuming it was seeded.
    /// </para>
    /// </summary>
    private async Task<int?> CreateOrResolveVersionAsync(
        string name, CreateRuleBody body, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage create = new(HttpMethod.Post, $"/rules?fabId={FabId}")
        {
            Content = JsonContent.Create(body),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage created = await http.SendAsync(create, cancellationToken);

        if (created.StatusCode != HttpStatusCode.Conflict)
        {
            created.EnsureSuccessStatusCode();
            return 0;
        }

        Option<RuleSummary> existing = await ReadRuleAsync(name, token, cancellationToken);
        if (!existing.HasValue || existing.Value.State != DraftState)
        {
            logger.RuleAlreadyExists(name);
            return null;
        }

        return existing.Value.Version;
    }

    /// <summary>
    /// The stored rule, or none when it cannot be read. A seeder that cannot
    /// tell a Draft from an Active rule leaves the fixable case unfixed, but
    /// it must not take the whole simulator down over it — the rule may have
    /// been archived deliberately, which is not ours to undo.
    /// </summary>
    private async Task<Option<RuleSummary>> ReadRuleAsync(
        string name, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage read = new(HttpMethod.Get, $"/rules/{name}?fabId={FabId}");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(read, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Option<RuleSummary>.None;
        }

        RuleSummary? summary = await response.Content
            .ReadFromJsonAsync<RuleSummary>(cancellationToken);

        return summary is null ? Option<RuleSummary>.None : Option<RuleSummary>.Some(summary);
    }

    /// <summary>
    /// The fab the simulator seeds into. Mirrors <c>MqttSampleMapper.FabId</c>
    /// — a rule must live in the same fab as the events it reacts to, or it
    /// will never fire (spec 013).
    /// </summary>
    private const string FabId = "munich";

    private const string DraftState = "Draft";

    /// <summary>
    /// Just the two fields the seeder acts on. Deliberately not the full
    /// <c>RuleDto</c> — the simulator is dev-only and must not gain a
    /// compile-time dependency on Automation's read model.
    /// </summary>
    private sealed record RuleSummary(int Version, string State);

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
