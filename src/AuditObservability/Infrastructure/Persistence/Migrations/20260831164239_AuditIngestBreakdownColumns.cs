using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditIngestBreakdownColumns : Migration
    {
        // **Two columns, and nothing else.** EF also emitted a drop-and-recreate
        // of PK_audit_events and ux_audit_event_identifier in exactly the shape
        // they already have — spurious churn from re-serialising the snapshot on
        // a newer provider. Removed by hand.
        //
        // That churn is untidy rather than dangerous, and the distinction was
        // measured: with the chunk compressed, both the DROP INDEX and the DROP
        // CONSTRAINT succeed.
        //
        // **The generated Down() was the real defect.** It re-added the primary
        // key on audit_id alone and the unique index on event_identifier alone.
        // TimescaleDB refuses that — "cannot create a unique index without the
        // column occurred_at (used in partitioning)" — which is why the initial
        // migration made both composite. That rollback could never have run.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No default expression. `clock_timestamp()` is not constant, and
            // adding a column with a non-constant default to a hypertable with
            // columnstore enabled fails with SqlState 0A000. The value is
            // supplied by the repository's insert instead.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "handler_entered_at",
                table: "audit_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "written_at",
                table: "audit_events",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handler_entered_at",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "written_at",
                table: "audit_events");
        }
    }
}
