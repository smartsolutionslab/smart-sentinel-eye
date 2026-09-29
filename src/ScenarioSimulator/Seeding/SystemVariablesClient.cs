using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Seeds a declared <see cref="VariableDefinition"/> over the SystemVariables
/// REST API (spec 289 plan.md §5.4), the same create-then-read-back-on-409
/// idiom <see cref="AutomationRulesClient"/> and <see cref="OverlayDesignerClient"/>
/// already use, but with no publish step: a system variable has no
/// draft/published lifecycle. Bearer via the scenario-simulator client_credentials
/// grant (scopes sse.variables.write + sse.variables.read).
/// </summary>
/// <remarks>
/// No client-side retry (ADR-0143): the default resilience handler already
/// leaves <c>POST</c>/<c>PATCH</c> unretried, and this client does not opt
/// back in — a genuine 5xx propagates rather than risking a second
/// half-applied create. The "no <c>RetryEveryMethod()</c>" half of the
/// guarantee lives in <c>ScenarioSeedingExtensions.AddScenarioSeeding</c>'s
/// registration.
/// </remarks>
public sealed class SystemVariablesClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    ILogger<SystemVariablesClient> logger)
{
    internal async Task<VariableSeedResult> EnsureVariableAsync(
        VariableDefinition definition, CancellationToken cancellationToken)
    {
        Ensure.That(definition).IsNotNull();

        string token = await tokens.GetAccessTokenAsync(cancellationToken);

        using HttpRequestMessage create = new(HttpMethod.Post, $"/system-variables?fabId={FabId}")
        {
            Content = JsonContent.Create(new DefineVariableBody(
                definition.Name, definition.Type, null, definition.TruthyLabel, definition.FalsyLabel)),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage created = await http.SendAsync(create, cancellationToken);

        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            return await ResolveConflictAsync(definition, token, cancellationToken);
        }

        if (created.StatusCode == HttpStatusCode.BadRequest)
        {
            return await RefuseFromProblemDetailsAsync(definition.Name, created, cancellationToken);
        }

        created.EnsureSuccessStatusCode();
        logger.VariableSeeded(definition.Name);
        return new VariableSeedResult.Seeded();
    }

    private async Task<VariableSeedResult> ResolveConflictAsync(
        VariableDefinition definition, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage read = new(HttpMethod.Get, $"/system-variables/{definition.Name}?fabId={FabId}");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(read, cancellationToken);
        response.EnsureSuccessStatusCode();

        VariableSummary existing = await response.Content.ReadFromJsonAsync<VariableSummary>(cancellationToken)
            ?? throw new InvalidOperationException($"Variable '{definition.Name}' conflicted but could not be read back.");

        if (string.Equals(existing.Type, definition.Type, StringComparison.Ordinal))
        {
            logger.VariableAlreadyExists(definition.Name);
            return new VariableSeedResult.Seeded();
        }

        string reason =
            $"variable '{definition.Name}' already exists as {existing.Type ?? "an unknown type"}, but this asset declares it as {definition.Type}.";
        logger.VariableRefused(definition.Name, reason);
        return new VariableSeedResult.Refused(reason);
    }

    /// <summary>
    /// The problem-detail's <c>detail</c>, or a generic reason when the 400
    /// body is empty or not JSON — this client must never throw on a 400 (S7).
    /// </summary>
    private async Task<VariableSeedResult> RefuseFromProblemDetailsAsync(
        string name, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string reason;
        try
        {
            ProblemDetailsBody? problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(cancellationToken);
            reason = problem?.Detail ?? "the request was rejected (400) with no problem detail.";
        }
        catch (JsonException)
        {
            reason = "the request was rejected (400) with no problem detail.";
        }

        logger.VariableRefused(name, reason);
        return new VariableSeedResult.Refused(reason);
    }

    /// <summary>
    /// The fab the simulator seeds into. Mirrors <c>AutomationRulesClient.FabId</c>.
    /// </summary>
    private const string FabId = "munich";

    private sealed record DefineVariableBody(string Name, string Type, string? InitialValue, string? TruthyLabel, string? FalsyLabel);

    /// <summary>
    /// Just the field the seeder acts on. Deliberately not the full read model —
    /// the simulator is dev-only and must not gain a compile-time dependency on
    /// SystemVariables' read model. Nullable: a 409 read-back with no <c>type</c>
    /// field deserializes to null rather than throwing, and the mismatch check
    /// below then correctly refuses it as "an unknown type" instead of blank.
    /// </summary>
    private sealed record VariableSummary(string? Type);

    /// <summary>Just the field this client acts on — <c>Title</c>/<c>Status</c> are unused.</summary>
    private sealed record ProblemDetailsBody(string? Detail);
}

/// <summary>Whether a declared <see cref="VariableDefinition"/> was seeded or refused.</summary>
internal abstract record VariableSeedResult
{
    /// <summary>Either created (201) or reused (409 + matching type) — the caller does not distinguish.</summary>
    internal sealed record Seeded : VariableSeedResult;

    internal sealed record Refused(string Reason) : VariableSeedResult;
}
