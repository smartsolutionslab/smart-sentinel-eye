using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaseInsensitiveCameraNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_cameras_fab_name_active",
                table: "cameras");

            migrationBuilder.AddColumn<string>(
                name: "name_normalized",
                table: "cameras",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                computedColumnSql: "upper(name)",
                stored: true);

            // #1434, acceptance criterion 5. A database already holding two
            // active cameras whose names differ only in case cannot take this
            // index, and CreateIndex would report that as a bare unique
            // violation naming the index — true, and useless to whoever has to
            // act on it at 3am.
            //
            // This refuses first and says which cameras collide, in which fab.
            // Deliberately NOT auto-reconciled: the fixes available to a
            // migration are renaming a camera or decommissioning it, and both
            // silently change what an operator sees on a wall of live video.
            // That is an operator's decision, not a deploy step's. Failing with
            // the list is the honest outcome; failing without it is the one
            // worth removing.
            migrationBuilder.Sql("""
                DO $$
                DECLARE collisions text;
                BEGIN
                    SELECT string_agg(format('fab %s: %s (%s cameras)', fab, normalized, tally), '; ')
                    INTO collisions
                    FROM (
                        SELECT fab, upper(name) AS normalized, count(*) AS tally
                        FROM cameras
                        WHERE status <> 'Decommissioned'
                        GROUP BY fab, upper(name)
                        HAVING count(*) > 1
                    ) AS duplicates;

                    IF collisions IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Camera names must be unique per fab ignoring case (#1434), but these already collide: %',
                            collisions
                            USING HINT =
                                'Rename or decommission all but one camera in each group, then re-run the migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_cameras_fab_name_normalized_active",
                table: "cameras",
                columns: new[] { "fab", "name_normalized" },
                unique: true,
                filter: "status <> 'Decommissioned'")
                .Annotation("Npgsql:IndexMethod", "btree");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_cameras_fab_name_normalized_active",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "name_normalized",
                table: "cameras");

            migrationBuilder.CreateIndex(
                name: "ux_cameras_fab_name_active",
                table: "cameras",
                columns: new[] { "fab", "name" },
                unique: true,
                filter: "status <> 'Decommissioned'")
                .Annotation("Npgsql:IndexMethod", "btree");
        }
    }
}
