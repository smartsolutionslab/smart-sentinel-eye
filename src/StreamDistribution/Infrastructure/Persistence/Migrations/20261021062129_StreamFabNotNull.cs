using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A stream's fab is its camera's, so the column is required. There is no
    /// backfill: the cameras live in another database, so no statement here can
    /// derive a fab. Where a row still has none, this refuses and names the
    /// streams rather than repairing or deleting them — the same call as
    /// CameraCatalog's CaseInsensitiveCameraNames: a deploy step must not
    /// silently decide what an operator sees, and these rows carry live video
    /// (ADR-0116). The refusal aborts the migration's transaction, so the column
    /// stays nullable and the next run retries from the same state.
    /// </summary>
    public partial class StreamFabNotNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE tally integer;
                DECLARE unattributed text;
                BEGIN
                    SELECT count(*), string_agg(format('stream %s (camera %s)', stream_id, camera_id), '; ' ORDER BY camera_id)
                    INTO tally, unattributed
                    FROM streams
                    WHERE fab IS NULL;

                    IF tally > 0 THEN
                        RAISE EXCEPTION
                            'streams.fab cannot be made NOT NULL (#1467): % stream(s) still have no fab: %',
                            tally, unattributed
                            USING HINT =
                                'Count the gap with SELECT count(*) FROM streams WHERE fab IS NULL, '
                                || 'then set each stream''s fab to its camera''s fab from camera-catalog-db '
                                || '(SELECT camera_id, fab FROM cameras), and re-run the migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "streams",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "streams",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
