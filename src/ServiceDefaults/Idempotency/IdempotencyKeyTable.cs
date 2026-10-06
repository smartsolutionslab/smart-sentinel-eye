using Microsoft.EntityFrameworkCore.Migrations;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// The <c>idempotency_key</c> table's DDL, written once (ADR-0142).
///
/// <para>
/// Every context that adopts a key needs the same table in its own schema, and
/// seven hand-copied <c>CREATE TABLE</c> statements is seven chances for one to
/// drift — a column widened here, an index forgotten there, and the store's SQL
/// silently means something different against one database than another.
/// </para>
///
/// <para>
/// Raw SQL and no EF entity, following <c>AddVariableValueRequestDedup</c>:
/// nothing reads this table through the change tracker, because the claim has to
/// be atomic against concurrent retries of one key.
/// </para>
/// </summary>
public static class IdempotencyKeyTable
{
    /// <summary>
    /// <para>
    /// <c>caller</c> is part of the primary key rather than a column beside it. A
    /// key is a string the caller invents, so two callers will collide; keyed on
    /// the string alone the second would replay the first's answer.
    /// </para>
    ///
    /// <para>
    /// <c>resource_identifier</c> is nullable on purpose and carries the three
    /// states the mechanism needs: no row is a first arrival, a row with a null
    /// identifier is an attempt still running, and a row with one is a completed
    /// attempt whose answer can be rebuilt. No response body is stored.
    /// </para>
    /// </summary>
    public static void Create(MigrationBuilder migrationBuilder)
    {
        Ensure.That(migrationBuilder).IsNotNull();

        migrationBuilder.Sql("""
            CREATE TABLE idempotency_key (
                key                  VARCHAR(128) NOT NULL,
                endpoint             VARCHAR(128) NOT NULL,
                caller               VARCHAR(256) NOT NULL,
                resource_identifier  UUID         NULL,
                reserved_at          TIMESTAMPTZ  NOT NULL,
                completed_at         TIMESTAMPTZ  NULL,
                PRIMARY KEY (key, endpoint, caller)
            );
            """);
        migrationBuilder.Sql("""
            CREATE INDEX ix_idempotency_key_reserved_at
                ON idempotency_key (reserved_at);
            """);
    }

    public static void Drop(MigrationBuilder migrationBuilder)
    {
        Ensure.That(migrationBuilder).IsNotNull();

        migrationBuilder.Sql("DROP TABLE IF EXISTS idempotency_key;");
    }

    /// <summary>
    /// Spec 302 (#2424/#2492). Adds the two columns a request binding needs.
    /// <see cref="Create"/> is not widened to include them: seven historical
    /// migrations call it, and widening it would make this <c>ALTER TABLE</c>
    /// fail on a fresh database with "column already exists".
    ///
    /// <para>
    /// <c>fab VARCHAR(64)</c>: every context's <c>FabIdentifier</c> maximum
    /// length is 32; 64 is headroom, not a guess at a new rule. Nullable
    /// because OverlayDesigner has no fab.
    /// </para>
    ///
    /// <para>
    /// <c>request_fingerprint CHAR(64)</c>: nullable only for legacy rows —
    /// every row written after this migration always sets it, and a stored
    /// <c>NULL</c> never equals anything (fail closed).
    /// </para>
    /// </summary>
    public static void AddRequestBinding(MigrationBuilder migrationBuilder)
    {
        Ensure.That(migrationBuilder).IsNotNull();

        migrationBuilder.Sql("""
            ALTER TABLE idempotency_key
                ADD COLUMN fab                 VARCHAR(64) NULL,
                ADD COLUMN request_fingerprint CHAR(64)    NULL;
            """);
    }

    /// <summary>The inverse of <see cref="AddRequestBinding"/>.</summary>
    public static void DropRequestBinding(MigrationBuilder migrationBuilder)
    {
        Ensure.That(migrationBuilder).IsNotNull();

        migrationBuilder.Sql("""
            ALTER TABLE idempotency_key
                DROP COLUMN IF EXISTS request_fingerprint,
                DROP COLUMN IF EXISTS fab;
            """);
    }
}
