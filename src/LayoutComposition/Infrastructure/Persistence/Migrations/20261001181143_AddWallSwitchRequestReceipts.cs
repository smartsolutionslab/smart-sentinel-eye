using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 296 FR-012: dedup table for <c>WallSceneSwitchRequestedV1Handler</c>,
    /// mirroring SystemVariables' <c>variable_value_request_dedup</c> (spec 007
    /// FR-018 precedent). The unique key is <c>(rule_id, causing_event_id)</c> —
    /// the rule is part of the key so two different rules firing on one causing
    /// event both apply (#2214's lesson, restated for walls). <c>wall_id</c> and
    /// <c>received_at</c> are carried for diagnosis only, never queried by the
    /// store itself. Raw-SQL managed like its precedent — no entity type, no
    /// model snapshot entry, so <c>dotnet ef</c> scaffolded this empty and the
    /// body is hand-written.
    /// </summary>
    public partial class AddWallSwitchRequestReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE wall_switch_request_receipts (
                    rule_id            UUID        NOT NULL,
                    causing_event_id   UUID        NOT NULL,
                    wall_id            UUID        NOT NULL,
                    received_at        TIMESTAMPTZ NOT NULL,
                    PRIMARY KEY (rule_id, causing_event_id)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS wall_switch_request_receipts;");
        }
    }
}
