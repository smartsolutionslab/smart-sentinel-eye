using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 300 (#2349) / ADR-0165 — a revision element widens from
    /// text-only to a closed kind + colour model (<c>Text</c>, <c>Box</c>,
    /// <c>Ellipse</c>). Reshapes <c>overlay_revision_labels</c> in place: no
    /// row is dropped or rewritten, only renamed and widened. Every existing
    /// row keeps reading back as the <c>Text</c> element it always was, at
    /// the colour (<c>#FFFFFFD9</c>) the frontend already renders by default
    /// for an unset colour (plan.md, "Migration").
    /// </summary>
    public partial class AddOverlayElementKindAndColour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rename the table: a label is now one kind of element.
            migrationBuilder.RenameTable(
                name: "overlay_revision_labels",
                newName: "overlay_revision_elements");

            // Postgres's RENAME TABLE does not rename the constraints that
            // live on it — the PK and FK survive under their
            // overlay_revision_labels-era names while EF's model now expects
            // the overlay_revision_elements convention. Left alone, the next
            // migration that touches either key would emit a DropPrimaryKey/
            // DropForeignKey naming the new-convention name and fail against
            // a real database (phase-6 finding S3).
            migrationBuilder.Sql(
                """ALTER TABLE overlay_revision_elements RENAME CONSTRAINT "PK_overlay_revision_labels" TO "PK_overlay_revision_elements";""");

            migrationBuilder.Sql(
                """ALTER TABLE overlay_revision_elements RENAME CONSTRAINT "FK_overlay_revision_labels_overlay_revisions_revision_id" TO "FK_overlay_revision_elements_overlay_revisions_revision_id";""");

            // 2. Rename columns onto the shape every kind now shares.
            migrationBuilder.RenameColumn(
                name: "label_x",
                table: "overlay_revision_elements",
                newName: "x");

            migrationBuilder.RenameColumn(
                name: "label_y",
                table: "overlay_revision_elements",
                newName: "y");

            migrationBuilder.RenameColumn(
                name: "label_width",
                table: "overlay_revision_elements",
                newName: "width");

            migrationBuilder.RenameColumn(
                name: "label_height",
                table: "overlay_revision_elements",
                newName: "height");

            migrationBuilder.RenameColumn(
                name: "label_text",
                table: "overlay_revision_elements",
                newName: "text");

            migrationBuilder.RenameColumn(
                name: "label_font_size_px",
                table: "overlay_revision_elements",
                newName: "font_size_px");

            // 3. Text and FontSizePx become optional: present iff Kind ==
            //    Text (ADR-0165 §1). Every existing row is a Text element
            //    today, so no value becomes null here.
            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "overlay_revision_elements",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<int>(
                name: "font_size_px",
                table: "overlay_revision_elements",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // 4. New kind + colour columns, backfilled by their defaults so
            //    every existing row becomes a Text element at #FFFFFFD9 —
            //    the nearest 8-bit alpha to --color-bg-label, so today's
            //    rendering does not move. The defaults are dropped
            //    immediately after: no future write may rely on either
            //    (FR-003).
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "overlay_revision_elements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Text");

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "overlay_revision_elements",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "#FFFFFFD9");

            migrationBuilder.Sql(
                "ALTER TABLE overlay_revision_elements ALTER COLUMN kind DROP DEFAULT;");

            migrationBuilder.Sql(
                "ALTER TABLE overlay_revision_elements ALTER COLUMN color DROP DEFAULT;");

            // 5. CHECK constraints — the belt-and-braces role
            //    ux_overlay_revisions_one_published already plays for the
            //    aggregate's lifecycle rule, now for the element's shape.
            //    Validated against existing rows, which all satisfy every
            //    one of these after step 4.
            // Two independent equivalences, not one over their conjunction:
            // `(kind = 'Text') = (A AND B)` is satisfied by a Box with
            // exactly one of text/font_size_px set (false = false), which
            // FR-004 forbids. Each component must match the kind on its own
            // (phase-6 finding S1).
            migrationBuilder.Sql(
                """
                ALTER TABLE overlay_revision_elements
                    ADD CONSTRAINT ck_overlay_revision_elements_text_matches_kind
                    CHECK ((kind = 'Text') = (text IS NOT NULL) AND (kind = 'Text') = (font_size_px IS NOT NULL));
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE overlay_revision_elements
                    ADD CONSTRAINT ck_overlay_revision_elements_kind_known
                    CHECK (kind IN ('Text', 'Box', 'Ellipse'));
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE overlay_revision_elements
                    ADD CONSTRAINT ck_overlay_revision_elements_color_format
                    CHECK (color ~ '^#[0-9A-F]{8}$');
                """);

            // Coarse text-alpha floor: 75 is the lowest alpha at which any
            // text colour (white) is legible, so this is a necessary
            // condition and never rejects a row the domain would accept.
            // The exact per-colour rule lives only in TextLegibility and its
            // TypeScript mirror, pinned by the shared vectors — not repeated
            // a third time in SQL.
            migrationBuilder.Sql(
                """
                ALTER TABLE overlay_revision_elements
                    ADD CONSTRAINT ck_overlay_revision_elements_text_alpha_floor
                    CHECK (kind <> 'Text' OR substring(color from 8 for 2) COLLATE "C" >= '75');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE overlay_revision_elements DROP CONSTRAINT ck_overlay_revision_elements_text_alpha_floor;");

            migrationBuilder.Sql(
                "ALTER TABLE overlay_revision_elements DROP CONSTRAINT ck_overlay_revision_elements_color_format;");

            migrationBuilder.Sql(
                "ALTER TABLE overlay_revision_elements DROP CONSTRAINT ck_overlay_revision_elements_kind_known;");

            migrationBuilder.Sql(
                "ALTER TABLE overlay_revision_elements DROP CONSTRAINT ck_overlay_revision_elements_text_matches_kind;");

            migrationBuilder.DropColumn(
                name: "color",
                table: "overlay_revision_elements");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "overlay_revision_elements");

            // A Box or Ellipse created after this migration has no text to
            // carry back onto the pre-cut shape, where every row was Text.
            // Reverting past that point is a local rollback aid, not a
            // lossless mirror (spec 150's Down carries the same caveat).
            migrationBuilder.Sql(
                "UPDATE overlay_revision_elements SET text = '' WHERE text IS NULL;");

            migrationBuilder.Sql(
                "UPDATE overlay_revision_elements SET font_size_px = 0 WHERE font_size_px IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "font_size_px",
                table: "overlay_revision_elements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "overlay_revision_elements",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "font_size_px",
                table: "overlay_revision_elements",
                newName: "label_font_size_px");

            migrationBuilder.RenameColumn(
                name: "text",
                table: "overlay_revision_elements",
                newName: "label_text");

            migrationBuilder.RenameColumn(
                name: "height",
                table: "overlay_revision_elements",
                newName: "label_height");

            migrationBuilder.RenameColumn(
                name: "width",
                table: "overlay_revision_elements",
                newName: "label_width");

            migrationBuilder.RenameColumn(
                name: "y",
                table: "overlay_revision_elements",
                newName: "label_y");

            migrationBuilder.RenameColumn(
                name: "x",
                table: "overlay_revision_elements",
                newName: "label_x");

            migrationBuilder.Sql(
                """ALTER TABLE overlay_revision_elements RENAME CONSTRAINT "FK_overlay_revision_elements_overlay_revisions_revision_id" TO "FK_overlay_revision_labels_overlay_revisions_revision_id";""");

            migrationBuilder.Sql(
                """ALTER TABLE overlay_revision_elements RENAME CONSTRAINT "PK_overlay_revision_elements" TO "PK_overlay_revision_labels";""");

            migrationBuilder.RenameTable(
                name: "overlay_revision_elements",
                newName: "overlay_revision_labels");
        }
    }
}
