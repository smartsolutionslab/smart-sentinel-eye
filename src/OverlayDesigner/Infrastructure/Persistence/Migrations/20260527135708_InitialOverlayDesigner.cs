using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOverlayDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "overlays",
                columns: table => new
                {
                    overlay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlays", x => x.overlay_id);
                });

            migrationBuilder.CreateTable(
                name: "overlay_revisions",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    label_text = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    label_x = table.Column<decimal>(type: "numeric", nullable: false),
                    label_y = table.Column<decimal>(type: "numeric", nullable: false),
                    label_width = table.Column<decimal>(type: "numeric", nullable: false),
                    label_height = table.Column<decimal>(type: "numeric", nullable: false),
                    label_font_size_px = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    overlay_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_revisions", x => x.revision_id);
                    table.ForeignKey(
                        name: "FK_overlay_revisions_overlays_overlay_id",
                        column: x => x.overlay_id,
                        principalTable: "overlays",
                        principalColumn: "overlay_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_overlay_revisions_number",
                table: "overlay_revisions",
                columns: new[] { "overlay_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_overlay_revisions_one_published",
                table: "overlay_revisions",
                column: "overlay_id",
                unique: true,
                filter: "state = 'Published'");

            migrationBuilder.CreateIndex(
                name: "ix_overlays_name",
                table: "overlays",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overlay_revisions");

            migrationBuilder.DropTable(
                name: "overlays");
        }
    }
}
