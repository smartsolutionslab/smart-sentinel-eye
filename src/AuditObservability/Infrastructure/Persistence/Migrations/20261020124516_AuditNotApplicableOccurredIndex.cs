using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditNotApplicableOccurredIndex : Migration
    {
        // #3035, owner decision 2026-10-19: unmeasured, no live EXPLAIN; CI integration
        // is the only evidence. No CHECK on the fab/attribution invariant (not decided).
        // Plain CREATE INDEX, not CONCURRENTLY: TimescaleDB does not support the latter
        // on a hypertable. Behaviour on compressed chunks is unverified.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_not_applicable_occurred",
                table: "audit_events",
                column: "occurred_at",
                filter: "fab_attribution = 'NotApplicable'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_not_applicable_occurred",
                table: "audit_events");
        }
    }
}
