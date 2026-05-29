using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "registered_clients",
                columns: table => new
                {
                    registered_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fab = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_clients", x => x.registered_client_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_registered_clients_kind_fab_disabled",
                table: "registered_clients",
                columns: new[] { "kind", "fab", "disabled_at" });

            migrationBuilder.CreateIndex(
                name: "ux_registered_clients_clientid_active",
                table: "registered_clients",
                column: "client_id",
                unique: true,
                filter: "disabled_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registered_clients");
        }
    }
}
