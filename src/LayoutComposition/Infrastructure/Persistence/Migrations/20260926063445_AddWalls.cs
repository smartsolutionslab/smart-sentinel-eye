using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 258 US1 (PD-2). <c>ux_walls_fab_name_ci</c> is created here as a
    /// hand-written expression index on <c>(fab, lower(name))</c> rather than
    /// through <c>migrationBuilder.CreateIndex</c>'s plain-column form the
    /// scaffolder emitted: EF's fluent <c>HasIndex</c> has no way to describe
    /// a Postgres expression index, so <c>WallConfiguration</c> declares the
    /// index by name only and this migration is its single source of truth.
    /// Case-insensitive on purpose — a wall's name is prominent on the
    /// picker UI, so "Line 3" and "line 3" should not coexist as two walls.
    /// </summary>
    public partial class AddWalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "walls",
                columns: table => new
                {
                    wall_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fab = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scenes = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    showing_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_version = table.Column<long>(type: "bigint", nullable: false),
                    showing_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_walls", x => x.wall_id);
                });

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_walls_fab_name_ci
                    ON walls (fab, lower(name));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "walls");
        }
    }
}
