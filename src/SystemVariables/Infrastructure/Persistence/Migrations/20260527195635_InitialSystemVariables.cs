using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSystemVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_variables",
                columns: table => new
                {
                    variable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    value_packed = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    truthy_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    falsy_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_variables", x => x.variable_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_system_variables_name_active",
                table: "system_variables",
                column: "name",
                unique: true,
                filter: "state <> 'Archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_variables");
        }
    }
}
