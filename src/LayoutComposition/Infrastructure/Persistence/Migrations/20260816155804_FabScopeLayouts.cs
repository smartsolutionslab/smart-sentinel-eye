using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FabScopeLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_layouts_name",
                table: "layouts");

            // 1 — add nullable. The scaffolded form was
            // `nullable: false, defaultValue: ""`, which writes fab = '' to
            // every existing row. That is not a valid FabIdentifier (minimum
            // length 2), so every layout would throw on the next read. Spec
            // 015 caught the identical scaffold; hand-corrected here to the
            // three-step add/backfill/tighten.
            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "layouts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // 2 — backfill. Layouts created before this feature belong to the
            // single live fab; leaving them unattributed would hide every one
            // of them from every operator, and every kiosk would go dark.
            //
            // Warns rather than fails: refusing would block a deployment whose
            // layouts really are munich's, which is every deployment that
            // exists. The warning is for the one case this cannot detect.
            //
            // Unlike spec 016's, this backfill *can* be SQL — layouts live in
            // this context's own database, so nothing has to be derived from
            // another one at runtime.
            migrationBuilder.Sql("""
                DO $$
                DECLARE attributed integer;
                BEGIN
                    UPDATE layouts SET fab = 'munich' WHERE fab IS NULL;
                    GET DIAGNOSTICS attributed = ROW_COUNT;
                    IF attributed > 0 THEN
                        RAISE WARNING
                            'FabScopeLayouts attributed % pre-existing layout(s) to fab ''munich''. If this database belongs to another fab, those layouts are now invisible to every operator of it.',
                            attributed;
                    END IF;
                END $$;
                """);

            // 3 — now the constraint can hold.
            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "layouts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            // Pre-existing tiles are deliberately NOT re-validated against
            // FR-014 here. After the guess above, a layout may hold a tile
            // whose camera is in another fab — but that mismatch is this
            // migration's own doing, not an operator's, and failing over it
            // would block the deployment this exists to fix (FR-018).

            migrationBuilder.CreateIndex(
                name: "ix_layouts_fab",
                table: "layouts",
                column: "fab");

            migrationBuilder.CreateIndex(
                name: "ix_layouts_fab_name",
                table: "layouts",
                columns: new[] { "fab", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_layout_revision_tiles_overlay_id",
                table: "layout_revision_tiles",
                column: "overlay_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_layouts_fab",
                table: "layouts");

            migrationBuilder.DropIndex(
                name: "ix_layouts_fab_name",
                table: "layouts");

            migrationBuilder.DropIndex(
                name: "ix_layout_revision_tiles_overlay_id",
                table: "layout_revision_tiles");

            migrationBuilder.DropColumn(
                name: "fab",
                table: "layouts");

            migrationBuilder.CreateIndex(
                name: "ix_layouts_name",
                table: "layouts",
                column: "name");
        }
    }
}
