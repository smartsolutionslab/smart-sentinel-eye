using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditIngestBreakdownColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "ux_audit_event_identifier",
                table: "audit_events");

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

            migrationBuilder.AddPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events",
                columns: new[] { "audit_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_event_identifier",
                table: "audit_events",
                columns: new[] { "event_identifier", "occurred_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "ux_audit_event_identifier",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "handler_entered_at",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "written_at",
                table: "audit_events");

            migrationBuilder.AddPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events",
                column: "audit_id");

            migrationBuilder.CreateIndex(
                name: "ux_audit_event_identifier",
                table: "audit_events",
                column: "event_identifier",
                unique: true);
        }
    }
}
