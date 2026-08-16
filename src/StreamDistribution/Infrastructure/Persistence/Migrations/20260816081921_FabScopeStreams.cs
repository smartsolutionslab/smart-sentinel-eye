using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FabScopeStreams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "streams",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_streams_fab",
                table: "streams",
                column: "fab");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_streams_fab",
                table: "streams");

            migrationBuilder.DropColumn(
                name: "fab",
                table: "streams");
        }
    }
}
