using Microsoft.EntityFrameworkCore.Migrations;
using SmartSentinelEye.ServiceDefaults.Idempotency;

#nullable disable

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 302 (#2424/#2492): binds an idempotency key to the request it was
    /// first used for. <see cref="IdempotencyKeyTable.Create"/> is not edited —
    /// it already ran for this context — so the two columns arrive as an
    /// <c>ALTER TABLE</c> instead.
    /// </summary>
    public partial class AddIdempotencyRequestBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            IdempotencyKeyTable.AddRequestBinding(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            IdempotencyKeyTable.DropRequestBinding(migrationBuilder);
        }
    }
}
