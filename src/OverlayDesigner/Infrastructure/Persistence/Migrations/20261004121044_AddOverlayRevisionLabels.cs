using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 150 (#2345) / ADR-0164 — the clean cut. Reshapes a revision from
    /// a single flattened label into an ordered, non-empty set of 1..8
    /// labels. One self-contained migration (no read window): create the
    /// labels table → backfill every existing revision into a single
    /// <c>ordinal = 0</c> label carrying its old six <c>label_*</c> columns
    /// → drop those columns. Zero data loss, mirroring ADR-0112 §3's
    /// <c>MultiTileLayouts</c>. <c>Down</c> reverses the backfill so a local
    /// rollback is possible.
    /// </summary>
    public partial class AddOverlayRevisionLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New owned labels table, composite PK (revision_id, ordinal).
            migrationBuilder.CreateTable(
                name: "overlay_revision_labels",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    label_text = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    label_x = table.Column<decimal>(type: "numeric", nullable: false),
                    label_y = table.Column<decimal>(type: "numeric", nullable: false),
                    label_width = table.Column<decimal>(type: "numeric", nullable: false),
                    label_height = table.Column<decimal>(type: "numeric", nullable: false),
                    label_font_size_px = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_revision_labels", x => new { x.revision_id, x.ordinal });
                    table.ForeignKey(
                        name: "FK_overlay_revision_labels_overlay_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "overlay_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 2. Backfill: every existing revision becomes one label at
            //    ordinal 0 carrying its old six columns. Every revision has
            //    exactly one today and the columns are NOT NULL, so this is
            //    total and cannot produce an empty set.
            migrationBuilder.Sql(
                @"INSERT INTO overlay_revision_labels (revision_id, ordinal, label_text, label_x, label_y, label_width, label_height, label_font_size_px)
                  SELECT revision_id, 0, label_text, label_x, label_y, label_width, label_height, label_font_size_px FROM overlay_revisions;");

            // 3. Clean cut — drop the now-redundant legacy scalar columns.
            migrationBuilder.DropColumn(
                name: "label_font_size_px",
                table: "overlay_revisions");

            migrationBuilder.DropColumn(
                name: "label_height",
                table: "overlay_revisions");

            migrationBuilder.DropColumn(
                name: "label_text",
                table: "overlay_revisions");

            migrationBuilder.DropColumn(
                name: "label_width",
                table: "overlay_revisions");

            migrationBuilder.DropColumn(
                name: "label_x",
                table: "overlay_revisions");

            migrationBuilder.DropColumn(
                name: "label_y",
                table: "overlay_revisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the legacy columns (NOT NULL ones need a temporary
            // default to land on existing rows).
            migrationBuilder.AddColumn<int>(
                name: "label_font_size_px",
                table: "overlay_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "label_height",
                table: "overlay_revisions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "label_text",
                table: "overlay_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "label_width",
                table: "overlay_revisions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "label_x",
                table: "overlay_revisions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "label_y",
                table: "overlay_revisions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Copy the ordinal-0 label back onto each revision.
            migrationBuilder.Sql(
                @"UPDATE overlay_revisions r
                  SET label_text = l.label_text, label_x = l.label_x, label_y = l.label_y,
                      label_width = l.label_width, label_height = l.label_height, label_font_size_px = l.label_font_size_px
                  FROM overlay_revision_labels l
                  WHERE l.revision_id = r.revision_id AND l.ordinal = 0;");

            migrationBuilder.DropTable(
                name: "overlay_revision_labels");
        }
    }
}
