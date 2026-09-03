using Microsoft.EntityFrameworkCore.Migrations;
using SmartSentinelEye.ServiceDefaults.Idempotency;

#nullable disable

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0142: the durable record of which idempotency keys have been claimed,
    /// in this context's own schema.
    ///
    /// <para>
    /// The DDL lives in <see cref="IdempotencyKeyTable"/> rather than here.
    /// Seven contexts need the same table, and seven hand-copied
    /// <c>CREATE TABLE</c> statements is seven chances for one to drift — at
    /// which point the store's SQL quietly means something different against one
    /// database than another.
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
