using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Migrations
{
    public partial class FabScopeCameras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "ux_cameras_name_lower",
                table: "cameras");

            // 1 — nullable, so the column can be added to a populated table.
            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "cameras",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // 2 — backfill. Cameras registered before this feature belong to
            // the single live fab; leaving them unattributed would hide every
            // one of them from every operator.
            //
            // Warns rather than fails: refusing would block a deployment whose
            // cameras really are munich's, which is every deployment that
            // exists. The warning is for the one case this cannot detect.
            migrationBuilder.Sql("""
                DO $$
                DECLARE attributed integer;
                BEGIN
                    UPDATE cameras SET fab = 'munich' WHERE fab IS NULL;
                    GET DIAGNOSTICS attributed = ROW_COUNT;
                    IF attributed > 0 THEN
                        RAISE WARNING
                            'FabScopeCameras attributed % pre-existing camera(s) to fab ''munich''. If this database belongs to another fab, those cameras are now invisible to every operator of it.',
                            attributed;
                    END IF;
                END $$;
                """);

            // 3 — now the constraint can hold.
            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "cameras",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            // 4 — the partial filter is NEW, not carried over: the old index
            // had none, so a decommissioned camera held its name forever.
            // Adopting it is what makes FR-003 true, and it is safe against
            // existing data because a partial unique index is strictly weaker
            // than the unfiltered one it replaces.
            migrationBuilder.CreateIndex(
                name: "ux_cameras_fab_name_active",
                table: "cameras",
                columns: new[] { "fab", "name" },
                unique: true,
                filter: "status <> 'Decommissioned'")
                .Annotation("Npgsql:IndexMethod", "btree");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Dropping the column discards which fab each camera belonged to,
            // and rolling forward again re-attributes every one to munich.
            // That is unrecoverable from inside the database, so a rollback
            // after cameras exist across fabs wants a dump taken first.
            //
            // Reverting can also fail where the forward migration succeeded:
            // two fabs may each hold a live camera of one name, which the
            // name-only index cannot represent. That is correct — the data
            // genuinely does not fit the old shape, and silently discarding
            // one of the two cameras would be worse.
            migrationBuilder.DropIndex(
                name: "ux_cameras_fab_name_active",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "fab",
                table: "cameras");

            migrationBuilder.CreateIndex(
                name: "ux_cameras_name_lower",
                table: "cameras",
                column: "name",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "btree");
        }
    }
}
