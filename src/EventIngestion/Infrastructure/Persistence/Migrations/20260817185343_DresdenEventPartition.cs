using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>dresden</c> list-partition to <c>events</c>.
    ///
    /// <para>
    /// <c>events</c> is <c>PARTITION BY LIST (fab_id)</c> and the initial
    /// migration seeded <c>munich</c> alone, on the reasoning that further fabs
    /// arrive through an admin operation at provisioning time. Nothing performs
    /// that operation, so an event for any other fab failed with
    /// <c>23514: no partition of relation "events" found for row</c>. Dresden
    /// has been a real fab in the realm since the isolation programme began
    /// (#1155); it was unreachable only because every write path took the fab
    /// from the caller unchecked and every test named munich.
    /// </para>
    ///
    /// <para>
    /// No monthly children here: <c>EventPartitionRolloverMigrator</c> discovers
    /// every list-partition under <c>events</c> and creates the current and next
    /// month beneath each before any Api service starts.
    /// </para>
    /// </summary>
    public partial class DresdenEventPartition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS events_dresden PARTITION OF events
                    FOR VALUES IN ('dresden')
                    PARTITION BY RANGE (ingested_at);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Dropping the partition drops dresden's events with it: the
            // partition is the storage, so there is no reversal that keeps them.
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS events_dresden;");
        }
    }
}
