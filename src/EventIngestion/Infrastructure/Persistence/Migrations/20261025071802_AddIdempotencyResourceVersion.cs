using Microsoft.EntityFrameworkCore.Migrations;
using SmartSentinelEye.ServiceDefaults.Idempotency;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 454 (#3096): records the version an idempotency key's answer carried, so
    /// a replay can tell the resource has since changed. <see cref="IdempotencyKeyTable.Create"/>
    /// is not edited - it already ran for this context - so the column arrives as an
    /// <c>ALTER TABLE</c> instead.
    /// </summary>
    public partial class AddIdempotencyResourceVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            IdempotencyKeyTable.AddResourceVersion(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            IdempotencyKeyTable.DropResourceVersion(migrationBuilder);
        }
    }
}
