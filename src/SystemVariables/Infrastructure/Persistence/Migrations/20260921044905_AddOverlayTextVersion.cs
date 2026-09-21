using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Issue #2426: the durable per-overlay push-version counter. Raw-SQL
    /// managed, like <c>variable_value_request_dedup</c> — no entity type,
    /// no model snapshot entry, so <c>dotnet ef</c> scaffolded this empty
    /// and the body is hand-written.
    /// </summary>
    public partial class AddOverlayTextVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            migrationBuilder.Sql("""
                CREATE TABLE overlay_text_version (
                    overlay_identifier  UUID    NOT NULL PRIMARY KEY,
                    version             BIGINT  NOT NULL
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            migrationBuilder.Sql("DROP TABLE IF EXISTS overlay_text_version;");
        }
    }
}
