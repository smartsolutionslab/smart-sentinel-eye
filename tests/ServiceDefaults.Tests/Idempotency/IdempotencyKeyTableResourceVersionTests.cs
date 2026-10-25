using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartSentinelEye.ServiceDefaults.Idempotency;

namespace SmartSentinelEye.ServiceDefaults.Tests.Idempotency;

/// <summary>
/// Spec 454 (#3096). The migration helper is the column's whole definition: seven contexts call
/// it, so its SQL is written out here by hand and compared, never read from the helper. The
/// real column is proved against Postgres by the CI-only store integration tests.
/// </summary>
public class IdempotencyKeyTableResourceVersionTests
{
    [Fact]
    public void Adding_the_resource_version_adds_one_nullable_integer_column_and_nothing_else()
    {
        MigrationBuilder builder = new("Npgsql.EntityFrameworkCore.PostgreSQL");

        IdempotencyKeyTable.AddResourceVersion(builder);

        Statements(builder).ShouldBe(["ALTER TABLE idempotency_key ADD COLUMN resource_version INTEGER NULL;"]);
    }

    [Fact]
    public void Dropping_the_resource_version_is_the_inverse_and_tolerates_a_missing_column()
    {
        MigrationBuilder builder = new("Npgsql.EntityFrameworkCore.PostgreSQL");

        IdempotencyKeyTable.DropResourceVersion(builder);

        Statements(builder).ShouldBe(["ALTER TABLE idempotency_key DROP COLUMN IF EXISTS resource_version;"]);
    }

    [Fact]
    public void Creating_the_table_is_not_widened_because_historical_migrations_call_it()
    {
        MigrationBuilder builder = new("Npgsql.EntityFrameworkCore.PostgreSQL");

        IdempotencyKeyTable.Create(builder);

        Statements(builder).ShouldAllBe(statement => !statement.Contains("resource_version", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Statements(MigrationBuilder builder) =>
        [.. builder.Operations
            .OfType<SqlOperation>()
            .Select(operation => string.Join(' ', operation.Sql.Split(
                [' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)))];
}
