using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 007 bridge: dedup table for
    /// <c>SystemVariableValueRequestedV1Handler</c>. The unique key is
    /// <c>(variable_name, causing_event_identifier)</c>; the
    /// <c>seen_at</c> column will drive a future 7-day-TTL cleanup
    /// worker.
    /// </summary>
    public partial class AddVariableValueRequestDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            migrationBuilder.Sql("""
                CREATE TABLE variable_value_request_dedup (
                    variable_name             VARCHAR(64) NOT NULL,
                    causing_event_identifier  UUID        NOT NULL,
                    seen_at                   TIMESTAMPTZ NOT NULL,
                    PRIMARY KEY (variable_name, causing_event_identifier)
                );
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_variable_value_request_dedup_seen_at
                    ON variable_value_request_dedup (seen_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            migrationBuilder.Sql("DROP TABLE IF EXISTS variable_value_request_dedup;");
        }
    }
}
