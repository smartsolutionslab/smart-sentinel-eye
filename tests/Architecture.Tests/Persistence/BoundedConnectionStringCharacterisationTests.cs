using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartSentinelEye.ServiceDefaults.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 282 (#1140), T011. Characterisation, captured green on unmodified
/// <c>develop</c> before T030.
///
/// <para>
/// <b>What this pins down.</b> Every context's created <c>DbContext</c> still
/// carries a connection string bounded by <see cref="PostgresConnectionBudget.MaxPoolSize"/>
/// (ADR-0125) after T030 wires the enrichment in. That is the reason
/// <c>EnrichNpgsqlDbContext</c> is used and not <c>AddNpgsqlDbContext</c>
/// (plan §2): <c>Add</c> would read <c>ConnectionStrings:{name}</c> itself and
/// bypass <see cref="PostgresConnectionBudget.GetBoundedPostgresConnectionString"/>
/// entirely. T030 must leave this test passing <b>unmodified</b> (constitution
/// §Testing).
/// </para>
///
/// <para>
/// The expected pool size is read from <see cref="PostgresConnectionBudget.MaxPoolSize"/>,
/// not reconstructed via <see cref="PostgresConnectionBudget.Bounded"/> — the
/// latter is the code under test, and asserting an input against its own
/// output would prove nothing (memory: an assertion must not check its own
/// input). The harness's configured connection string names no pool size, so
/// this also exercises the "apply the default" branch of <c>Bounded</c>.
/// </para>
/// </summary>
public class BoundedConnectionStringCharacterisationTests
{
    [Theory]
    [MemberData(nameof(PostgresComposition.AllContexts), MemberType = typeof(PostgresComposition))]
    public void The_created_db_context_connection_string_is_bounded_to_the_platform_pool_cap(string contextName)
    {
        using ServiceProvider provider = PostgresComposition.Compose(contextName);
        using IServiceScope scope = provider.CreateScope();
        using DbContext dbContext = PostgresComposition.ResolveDbContext(contextName, scope.ServiceProvider);

        string? connectionString = dbContext.Database.GetConnectionString();
        connectionString.ShouldNotBeNull($"{contextName}'s DbContext must carry a connection string.");

        NpgsqlConnectionStringBuilder written = new(connectionString);

        written.MaxPoolSize.ShouldBe(
            PostgresConnectionBudget.MaxPoolSize,
            $"{contextName}'s DbContext connection string is not bounded to "
            + $"PostgresConnectionBudget.MaxPoolSize ({PostgresConnectionBudget.MaxPoolSize}). "
            + "GetBoundedPostgresConnectionString must remain the only source of the connection "
            + "string (ADR-0125); Aspire's own connection-string resolution (AddNpgsqlDbContext) "
            + "is not used for exactly this reason (plan §2).");
    }
}
