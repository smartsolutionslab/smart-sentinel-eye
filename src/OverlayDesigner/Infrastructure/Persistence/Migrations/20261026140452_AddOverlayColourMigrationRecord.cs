using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverlayColourMigrationRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 463 (#3108), ADR-0190: one row per element colour the OverlayColourCapMigrator
            // darkened. A record read by administrators, not an entity, so the model is untouched.
            migrationBuilder.Sql("""
                CREATE TABLE overlay_colour_migrations (
                    revision_id uuid NOT NULL,
                    ordinal integer NOT NULL,
                    previous_color varchar(9) NOT NULL,
                    migrated_color varchar(9) NOT NULL,
                    migrated_at timestamp with time zone NOT NULL,
                    PRIMARY KEY (revision_id, ordinal)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE overlay_colour_migrations;");
        }
    }
}
