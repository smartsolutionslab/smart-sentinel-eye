using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditObservability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 009 ADR-0101: the AuditObservability database
            // uses TimescaleDB to manage the 90-day hot tier. The
            // extension is idempotent — every other context's DB
            // on the same Postgres server is unaffected.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    audit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fab_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    event_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    resource_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    actor_identifier = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    event_identifier = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    payload_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    // Composite PK includes occurred_at: TimescaleDB requires
                    // the partitioning column in every unique index (TS103).
                    table.PrimaryKey("PK_audit_events", x => new { x.audit_id, x.occurred_at });
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_actor_occurred",
                table: "audit_events",
                columns: new[] { "actor_identifier", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_fab_occurred",
                table: "audit_events",
                columns: new[] { "fab_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_kind_occurred",
                table: "audit_events",
                columns: new[] { "event_kind", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_resource_occurred",
                table: "audit_events",
                columns: new[] { "resource_kind", "resource_identifier", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_event_identifier",
                table: "audit_events",
                columns: new[] { "event_identifier", "occurred_at" },
                unique: true);

            // Convert the table to a 1-month-chunked hypertable
            // (ADR-0101). `migrate_data => true` is unused at
            // initial creation (empty table) but keeps the call
            // idempotent across re-runs.
            migrationBuilder.Sql(
                "SELECT create_hypertable('audit_events', 'occurred_at', " +
                "chunk_time_interval => INTERVAL '1 month', " +
                "if_not_exists => true);");

            // In-place column compression for chunks older than
            // 30 days — keeps days 30–90 queryable without
            // application code changes (ADR-0101).
            migrationBuilder.Sql("ALTER TABLE audit_events SET (timescaledb.compress);");
            migrationBuilder.Sql(
                "SELECT add_compression_policy('audit_events', INTERVAL '30 days');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
