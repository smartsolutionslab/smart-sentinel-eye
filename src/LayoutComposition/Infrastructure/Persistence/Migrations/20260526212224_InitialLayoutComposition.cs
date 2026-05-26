using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLayoutComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "layouts",
                columns: table => new
                {
                    layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layouts", x => x.layout_id);
                });

            migrationBuilder.CreateTable(
                name: "layout_revisions",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    camera_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    layout_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layout_revisions", x => x.revision_id);
                    table.ForeignKey(
                        name: "FK_layout_revisions_layouts_layout_id",
                        column: x => x.layout_id,
                        principalTable: "layouts",
                        principalColumn: "layout_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_layout_revisions_number",
                table: "layout_revisions",
                columns: new[] { "layout_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_layout_revisions_one_published",
                table: "layout_revisions",
                column: "layout_id",
                unique: true,
                filter: "state = 'Published'");

            migrationBuilder.CreateIndex(
                name: "ix_layouts_name",
                table: "layouts",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "layout_revisions");

            migrationBuilder.DropTable(
                name: "layouts");
        }
    }
}
