using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 317 (#2325), plan.md §4. Adds the quarantine columns to the
    /// existing <c>dead_letters</c> row (spec.md §0.1 — the user's decision:
    /// a reason code and hold state, not a new aggregate), back-fills every
    /// existing row's <c>reason</c> by its topic prefix (FR-004), and resets
    /// every existing <c>discovery</c> declaration to undeclared (FR-014) —
    /// before this spec the two were behaviourally identical (Q1, option A),
    /// so the deletion preserves every deployment's behaviour exactly.
    /// </summary>
    public partial class AddDeadLetterHoldState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "dead_letters",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "dead_letters",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "dead_letters",
                type: "text",
                nullable: false,
                defaultValue: "Held");

            // FR-004. An "event/" topic is PersistenceLoopHostedService's own
            // synthesised shape for a parsed-and-refused envelope; anything
            // else — MqttSubscriberHostedService's raw MQTT topic — never
            // reached a parsed envelope. kind is left null: recoverable from
            // the error text in principle, but parsing free text in a
            // migration is not worth it for audit rows (A4).
            migrationBuilder.Sql(
                "UPDATE dead_letters SET reason = CASE WHEN topic LIKE 'event/%' THEN 'Refused' ELSE 'ParseFailure' END;");

            // FR-014. Source modes stores its mode lowercase (spec 269
            // FR-001, EventTypeMode.Discovery.Value == "discovery").
            migrationBuilder.Sql("DELETE FROM source_modes WHERE mode = 'discovery';");

            migrationBuilder.CreateIndex(
                name: "ix_dead_letters_fab_reason_state",
                table: "dead_letters",
                columns: new[] { "fab", "reason", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted source_modes rows are not restored (FR-014 — the
            // deletion is the point, not a side effect to undo) and the
            // back-filled reason/state values are not recoverable to "absent"
            // either. Down only drops what Up added.
            migrationBuilder.DropIndex(
                name: "ix_dead_letters_fab_reason_state",
                table: "dead_letters");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "dead_letters");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "dead_letters");

            migrationBuilder.DropColumn(
                name: "state",
                table: "dead_letters");
        }
    }
}
