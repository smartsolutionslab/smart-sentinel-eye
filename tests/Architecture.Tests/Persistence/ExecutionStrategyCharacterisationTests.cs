using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 282 (#1140), T010. Characterisation, captured green on unmodified
/// <c>develop</c> before T030 wires <c>Aspire.Npgsql.EntityFrameworkCore.PostgreSQL</c>
/// in through <c>EnrichNpgsqlDbContext</c>.
///
/// <para>
/// <b>What this pins down.</b> Every context's <c>DbContext</c> resolves EF
/// Core's non-retrying <c>NpgsqlExecutionStrategy</c>. That matters because
/// Wolverine's outbox uses <c>UseEntityFrameworkCoreTransactions</c>
/// (ADR-0088), a user-initiated transaction the retrying strategy
/// (<c>NpgsqlRetryingExecutionStrategy</c>, which Aspire's component switches
/// on by default via <c>EnableRetryOnFailure</c>) refuses outright. T030 must
/// leave this test passing <b>unmodified</b> — an edited assertion here is
/// evidence the wiring changed behaviour, not proof the wiring is done
/// (constitution §Testing).
/// </para>
///
/// <para>
/// The exact runtime type is asserted by its published full name rather than
/// by a compile-time reference to <c>Npgsql.EntityFrameworkCore.PostgreSQL
/// .Storage.Internal.NpgsqlExecutionStrategy</c>: that type lives in Npgsql's
/// own <c>Internal</c> namespace, and referencing it directly would either
/// need <c>#pragma warning disable EF1001</c> or fail the Release build
/// (ADR-0034 treats warnings as errors). The literal is a fact about the
/// published assembly, not the code under test (memory: an assertion must not
/// check its own input).
/// </para>
/// </summary>
public class ExecutionStrategyCharacterisationTests
{
    private const string NonRetryingStrategyTypeName =
        "Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy";

    [Theory]
    [MemberData(nameof(PostgresComposition.AllContexts), MemberType = typeof(PostgresComposition))]
    public void The_created_db_context_resolves_the_non_retrying_npgsql_execution_strategy(string contextName)
    {
        using ServiceProvider provider = PostgresComposition.Compose(contextName);
        using IServiceScope scope = provider.CreateScope();
        using DbContext dbContext = PostgresComposition.ResolveDbContext(contextName, scope.ServiceProvider);

        dbContext.Database.CreateExecutionStrategy().GetType().FullName.ShouldBe(
            NonRetryingStrategyTypeName,
            $"{contextName}'s DbContext must resolve the non-retrying NpgsqlExecutionStrategy. "
            + "The retrying strategy (NpgsqlRetryingExecutionStrategy) refuses Wolverine's "
            + "user-initiated EF transactions (ADR-0088, UseEntityFrameworkCoreTransactions) and "
            + "changes the exception shape OutboxBacklogHealthCheck handles (ADR-0154 §Context). "
            + "Aspire's EnrichNpgsqlDbContext enables it by default via EnableRetryOnFailure unless "
            + "DisableRetry is set, which is why this must stay true.");
    }
}
