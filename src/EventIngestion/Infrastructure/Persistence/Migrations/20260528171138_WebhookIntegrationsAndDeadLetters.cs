using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookIntegrationsAndDeadLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dead_letters",
                columns: table => new
                {
                    dead_letter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    raw_payload = table.Column<string>(type: "text", nullable: false),
                    error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letters", x => x.dead_letter_id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_integrations",
                columns: table => new
                {
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    default_kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_integrations", x => x.integration_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dead_letters_rejected_at",
                table: "dead_letters",
                column: "rejected_at");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_integrations_name",
                table: "webhook_integrations",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letters");

            migrationBuilder.DropTable(
                name: "webhook_integrations");
        }
    }
}
