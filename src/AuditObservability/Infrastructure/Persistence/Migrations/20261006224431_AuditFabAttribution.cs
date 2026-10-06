using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditFabAttribution : Migration
    {
        // Spec 306 (#2540). A constant default — unlike handler_entered_at /
        // written_at's clock_timestamp() in the precedent migration
        // (AuditIngestBreakdownColumns), which is non-constant and fails
        // outright on a columnstore-compressed hypertable (SqlState 0A000).
        // 'Unresolved' is the fail-safe value: SC-6 — a row this backfill
        // cannot otherwise classify is left over-reporting unresolved, never
        // silently relabelled not-applicable.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fab_attribution",
                table: "audit_events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unresolved");

            // Backfill (SC-9). The event_kind list here is frozen on purpose —
            // it records history, including OverlayRevisionPublishedV1/V2
            // which no longer exist in code (git log -S: V1 from db1189e6, V2
            // until 97e5e33f) — unlike the runtime register, FabNeutralEvents,
            // which only needs to know the three current publishers.
            migrationBuilder.Sql("""
                UPDATE audit_events SET fab_attribution = 'Resolved' WHERE fab_id IS NOT NULL;
                UPDATE audit_events SET fab_attribution = 'NotApplicable'
                 WHERE fab_id IS NULL AND event_kind IN (
                   'AuditChunkArchivedV1', 'OverlayRevisionArchivedV1',
                   'OverlayRevisionPublishedV1', 'OverlayRevisionPublishedV2', 'OverlayRevisionPublishedV3');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fab_attribution",
                table: "audit_events");
        }
    }
}
