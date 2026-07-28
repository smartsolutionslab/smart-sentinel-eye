using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistStreamSourceUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_url",
                table: "streams",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            // Rows written before this column existed have no recoverable source:
            // the URL lives in camera-catalog-db, a different database, so it
            // cannot be backfilled here. Left in place they would be worse than
            // useless — StreamSourceUrl.From("") throws, so the EF value
            // converter would fault on EVERY read of the streams table.
            //
            // A Stream is derived state: ProvisionStreamCommand rebuilds one from
            // CameraRegisteredV1, and the reconciler re-creates the MediaMTX path
            // from the row. Dropping the unusable rows is recoverable; leaving a
            // table that throws on load is not.
            migrationBuilder.Sql("DELETE FROM streams WHERE source_url = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_url",
                table: "streams");
        }
    }
}
