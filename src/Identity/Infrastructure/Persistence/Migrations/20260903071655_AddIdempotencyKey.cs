using Microsoft.EntityFrameworkCore.Migrations;
using SmartSentinelEye.ServiceDefaults.Idempotency;

#nullable disable

namespace SmartSentinelEye.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0142: the durable record of which idempotency keys have been claimed.
    ///
    /// <para>
    /// Raw SQL and no EF entity, following
    /// <c>AddVariableValueRequestDedup</c>. Nothing reads this table through the
    /// change tracker — the claim has to be atomic against concurrent retries of
    /// one key, which is <c>INSERT ... ON CONFLICT</c> doing it in a single
    /// statement.
    /// </para>
    ///
    /// <para>
    /// <c>caller</c> is part of the primary key, not a column beside it. A key is
    /// a string the caller invents, so two callers will collide; keyed on the
    /// string alone the second would replay the first's answer, which on
    /// <c>/devices/register</c> means another tenant's device and its secret.
    /// </para>
    ///
    /// <para>
    /// <c>resource_identifier</c> is nullable on purpose and carries the three
    /// states this mechanism needs: no row is a first arrival, a row with a null
    /// identifier is an attempt still running, and a row with one is a completed
    /// attempt whose answer can be rebuilt. No response body is stored, and no
    /// secret.
    /// </para>
    /// </summary>
    public partial class AddIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            IdempotencyKeyTable.Create(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            IdempotencyKeyTable.Drop(migrationBuilder);
        }
    }
}
