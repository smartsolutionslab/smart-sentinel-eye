using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// ADR-0154 clause 2: no <c>/health</c> check may name or reach an external
/// dependency. Scheduled <b>before</b> the Aspire.Npgsql wiring (plan §5.3,
/// spec A-3) — it is FR-003's safety net, not new behaviour to drive red,
/// because the behaviour it guards for <b>Postgres</b> (no dependency health
/// check registered) is already true today. Its bite is proven separately,
/// by counterfactual (SC-006): quoted in
/// <c>specs/282-the-reference-nobody-calls/T031-counterfactual.md</c> —
/// flipping <c>DisableHealthChecks</c> to <c>false</c> in
/// <c>PostgresTelemetry</c> turns all nine cases of this guard red, firing
/// both rule (a) and rule (b).
///
/// <para>
/// <b>A genuine, pre-existing violation found while writing this guard —
/// unrelated to Postgres, and out of this spec's scope.</b>
/// <c>audit-observability</c> was <b>not</b> green when this guard was first
/// written: <c>AddAzureBlobServiceClient("blobs")</c>
/// (<c>AuditObservabilityInfrastructureModule.cs</c>) registered
/// <c>HealthChecks.Azure.Storage.Blobs.AzureBlobStorageHealthCheck</c>, named
/// <c>"Azure_BlobServiceClient"</c>, and <c>Extensions.cs:171</c> maps
/// <c>/health</c> with no predicate, so every registered check — this one
/// included — was exposed there. Both rules in this guard fired on it
/// independently. That was exactly the gap ADR-0154's own Consequences
/// section names — <i>"Nothing guards a future check that violates clause
/// 2"</i> — and exactly what issue #2571 was the open follow-up for; it
/// predated spec 282 and this guard did not create it, only found it. It has
/// since been fixed by #2680 (merged to <c>develop</c>, disabling the
/// auto-registered Azure Blob health check), so this guard is now genuinely
/// 9/9 green rather than 8/9 with a documented exception.
/// </para>
///
/// <para>
/// <b>Two independent rules</b> (plan §5.3), because a name convention alone
/// is not durable and a type check alone gives no readable failure message:
/// </para>
///
/// <list type="bullet">
/// <item><b>(a) Name.</b> The registration's name contains, case-insensitively,
/// a term from a fixed dependency vocabulary, or matches the composed
/// context's own <c>DbContext</c> type name (Aspire's own check is named
/// after the <c>TContext</c> type parameter, e.g. <c>CameraCatalogDbContext</c>).</item>
/// <item><b>(b) Type.</b> The health check instance <c>registration.Factory</c>
/// produces comes from an assembly whose name starts with a dependency-probing
/// library's own: EF Core's <c>Microsoft.Extensions.Diagnostics.HealthChecks
/// .EntityFrameworkCore</c>, the AspNetCore.HealthChecks family
/// (<c>HealthChecks.</c>), Aspire's own components (<c>Aspire.</c>), or
/// <c>Npgsql</c> itself.</item>
/// </list>
///
/// <para>
/// <b>The vocabulary is a list, and that is stated rather than hidden.</b>
/// Clause 2 has no other checkable form than naming the dependencies this
/// platform actually has: <c>postgres</c>, <c>npgsql</c>, <c>dbcontext</c>,
/// <c>database</c>, <c>sql</c>, <c>rabbit</c>, <c>amqp</c>, <c>keycloak</c>,
/// <c>mqtt</c>, <c>mosquitto</c>, <c>mediamtx</c>, <c>blob</c>, <c>azurite</c>,
/// <c>redis</c>. A dependency named outside this list, or a check whose type
/// comes from neither rule's assemblies, is a gap rule (b) is meant to close
/// for library checks whatever their name — but a bespoke check with an
/// unvocabularied name and a type outside those assemblies would still slip
/// through. That risk is accepted rather than hidden (plan §7 risk table).
/// </para>
///
/// <para>
/// <b>Scope, stated for the reviewer (spec A-4, A-5).</b> This composes each
/// context's <c>Add{Context}Infrastructure</c> only — <c>AddServiceDefaults</c>
/// (which registers <c>self</c> and <c>revocation-snapshot</c>) is not
/// composed here, because it lives in each service's own <c>Program.cs</c>
/// and composing the real host is #2571's open question 1, not decided by
/// this guard. The existing <c>outbox-{module}</c> check (ADR-0154 §Context,
/// registered by <c>AddWolverineForContext</c>) does query Postgres and is
/// deliberately kept out of rule (a)'s reach by name and out of rule (b)'s
/// reach by assembly: it is <c>ServiceDefaults</c>' own, ADR-0154-reviewed
/// check, not a dependency-probing library check, and its <c>failureStatus</c>
/// is #2571's concern, not this guard's.
/// </para>
/// </summary>
public class DependencyHealthCheckGuardTests
{
    private static readonly string[] DependencyVocabulary =
    [
        "postgres", "npgsql", "dbcontext", "database", "sql",
        "rabbit", "amqp", "keycloak", "mqtt", "mosquitto",
        "mediamtx", "blob", "azurite", "redis",
    ];

    private static readonly string[] DependencyProbingAssemblyPrefixes =
    [
        "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore",
        "HealthChecks.",
        "Aspire.",
        "Npgsql",
    ];

    [Theory]
    [MemberData(nameof(PostgresComposition.AllContexts), MemberType = typeof(PostgresComposition))]
    public void No_registered_health_check_names_or_reaches_postgres_or_any_other_dependency(string contextName)
    {
        using ServiceProvider provider = PostgresComposition.Compose(contextName);
        using IServiceScope scope = provider.CreateScope();

        List<string> offenders = [.. Offenders(contextName, scope.ServiceProvider)];

        offenders.ShouldBeEmpty(
            $"{contextName} registers a health check that names or reaches an external dependency "
            + $"(ADR-0154 clause 2): {string.Join(", ", offenders)}. A /health check is a liveness "
            + "signal, not a readiness probe for shared infrastructure; a dependency outage must "
            + "route to telemetry instead, never to this endpoint.");
    }

    private static IEnumerable<string> Offenders(string contextName, IServiceProvider serviceProvider)
    {
        HealthCheckServiceOptions? options = serviceProvider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value;
        if (options is null)
        {
            yield break;
        }

        Type dbContextType = PostgresComposition.DbContextTypeFor(contextName);

        foreach (HealthCheckRegistration registration in options.Registrations)
        {
            bool nameOffends =
                DependencyVocabulary.Any(term => registration.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                || registration.Name.Equals(dbContextType.Name, StringComparison.OrdinalIgnoreCase);

            object instance = registration.Factory(serviceProvider);
            string? assemblyName = instance.GetType().Assembly.GetName().Name;
            bool typeOffends = assemblyName is not null
                && DependencyProbingAssemblyPrefixes.Any(
                    prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));

            if (nameOffends || typeOffends)
            {
                yield return $"{contextName}:\"{registration.Name}\" ({instance.GetType().FullName}, "
                    + $"name rule={nameOffends}, type rule={typeOffends})";
            }
        }
    }
}
