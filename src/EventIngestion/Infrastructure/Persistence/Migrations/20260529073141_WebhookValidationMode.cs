using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookValidationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "keycloak_client_id",
                table: "webhook_integrations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rotated_at",
                table: "webhook_integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "validation_mode",
                table: "webhook_integrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "keycloak_client_id",
                table: "webhook_integrations");

            migrationBuilder.DropColumn(
                name: "rotated_at",
                table: "webhook_integrations");

            migrationBuilder.DropColumn(
                name: "validation_mode",
                table: "webhook_integrations");
        }
    }
}
