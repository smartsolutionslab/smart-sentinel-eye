using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.Automation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    trigger_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trigger_kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    predicate = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    action_packed = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.rule_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rules_trigger_state",
                table: "rules",
                columns: new[] { "trigger_source", "trigger_kind", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_rules_name_active",
                table: "rules",
                column: "name",
                unique: true,
                filter: "state <> 'Archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rules");
        }
    }
}
