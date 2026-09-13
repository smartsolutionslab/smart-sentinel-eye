using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegisteredEventTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "registered_event_types",
                columns: table => new
                {
                    registered_event_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fab = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_event_types", x => x.registered_event_type_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registered_event_types_fab",
                table: "registered_event_types",
                column: "fab");

            migrationBuilder.CreateIndex(
                name: "ux_registered_event_types_fab_kind",
                table: "registered_event_types",
                columns: new[] { "fab", "kind" },
                unique: true,
                filter: "state <> 'Retired'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "registered_event_types");
        }
    }
}
