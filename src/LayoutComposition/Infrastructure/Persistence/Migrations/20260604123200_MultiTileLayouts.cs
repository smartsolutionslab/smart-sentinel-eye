using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 010 / ADR-0112 §3 — the clean V2 cut. Reshapes a revision from a
    /// single <c>camera_id</c> + optional <c>overlay_id</c> into a grid of
    /// tiles. One self-contained migration (no two-deploy read window):
    /// create the tiles table + add the grid columns → backfill every
    /// existing revision into a single <c>(0,0)</c> tile on a 1×1 grid →
    /// drop the legacy scalar columns. Zero data loss. <c>Down</c> reverses
    /// the backfill so a local rollback is possible.
    /// </summary>
    public partial class MultiTileLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New owned tiles table, composite PK (revision_id, row, col).
            migrationBuilder.CreateTable(
                name: "layout_revision_tiles",
                columns: table => new
                {
                    row = table.Column<int>(type: "integer", nullable: false),
                    col = table.Column<int>(type: "integer", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    camera_id = table.Column<Guid>(type: "uuid", nullable: false),
                    overlay_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layout_revision_tiles", x => new { x.revision_id, x.row, x.col });
                    table.ForeignKey(
                        name: "FK_layout_revision_tiles_layout_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "layout_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 2. Grid columns. Temporary default 1 so existing rows become a
            //    1×1 grid; the defaults are dropped in step 4.
            migrationBuilder.AddColumn<int>(
                name: "grid_rows",
                table: "layout_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "grid_cols",
                table: "layout_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // 3. Backfill: every existing revision becomes one tile at (0,0)
            //    carrying its old camera + (nullable) overlay. Deterministic,
            //    zero-loss.
            migrationBuilder.Sql(
                @"INSERT INTO layout_revision_tiles (revision_id, row, col, camera_id, overlay_id)
                  SELECT revision_id, 0, 0, camera_id, overlay_id FROM layout_revisions;");

            // 4. Drop the temporary grid defaults (they only existed to
            //    satisfy the NOT NULL backfill above).
            migrationBuilder.AlterColumn<int>(
                name: "grid_rows",
                table: "layout_revisions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "grid_cols",
                table: "layout_revisions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            // 5. Clean cut — drop the now-redundant legacy scalar columns.
            migrationBuilder.DropColumn(
                name: "camera_id",
                table: "layout_revisions");

            migrationBuilder.DropColumn(
                name: "overlay_id",
                table: "layout_revisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the legacy columns (camera_id NOT NULL needs a temporary
            // default to land on existing rows).
            migrationBuilder.AddColumn<Guid>(
                name: "camera_id",
                table: "layout_revisions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "overlay_id",
                table: "layout_revisions",
                type: "uuid",
                nullable: true);

            // Copy the (0,0) tile back onto each revision.
            migrationBuilder.Sql(
                @"UPDATE layout_revisions r
                  SET camera_id = t.camera_id, overlay_id = t.overlay_id
                  FROM layout_revision_tiles t
                  WHERE t.revision_id = r.revision_id AND t.row = 0 AND t.col = 0;");

            migrationBuilder.AlterColumn<Guid>(
                name: "camera_id",
                table: "layout_revisions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValue: Guid.Empty);

            migrationBuilder.DropTable(
                name: "layout_revision_tiles");

            migrationBuilder.DropColumn(
                name: "grid_cols",
                table: "layout_revisions");

            migrationBuilder.DropColumn(
                name: "grid_rows",
                table: "layout_revisions");
        }
    }
}
