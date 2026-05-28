using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEventIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Partitioned `events` table (spec 006 FR-012). Postgres
            // requires partition keys in the primary key, hence the
            // composite (fab_id, event_id, ingested_at). Hybrid
            // idempotency (FR-002) is backed by a separate UNIQUE
            // (fab_id, event_id) index that excludes ingested_at —
            // the migration creates it explicitly below since EF's
            // model builder cannot express a unique index that
            // ignores a PK column.
            migrationBuilder.Sql(@"
                CREATE TABLE events (
                    fab_id      VARCHAR(32)  NOT NULL,
                    event_id    UUID         NOT NULL,
                    ingested_at TIMESTAMPTZ  NOT NULL,
                    source      VARCHAR(16)  NOT NULL,
                    device_id   VARCHAR(64)  NOT NULL,
                    kind        VARCHAR(128) NOT NULL,
                    occurred_at TIMESTAMPTZ  NOT NULL,
                    payload     JSONB        NOT NULL,
                    version     INTEGER      NOT NULL,
                    PRIMARY KEY (fab_id, event_id, ingested_at)
                ) PARTITION BY LIST (fab_id);
            ");

            // Per-fab list partition for 'munich'. Additional fabs are
            // added by an admin operation at provisioning time; the
            // initial migration seeds munich because it's the v1
            // single-fab deployment.
            migrationBuilder.Sql(@"
                CREATE TABLE events_munich PARTITION OF events
                    FOR VALUES IN ('munich')
                    PARTITION BY RANGE (ingested_at);
            ");

            // Monthly range partition. The MigrationRunner partition-
            // rollover cron job (spec 006 T108) creates the next
            // month's partition daily before the 1st.
            migrationBuilder.Sql(@"
                CREATE TABLE events_munich_202605 PARTITION OF events_munich
                    FOR VALUES FROM ('2026-05-01') TO ('2026-06-01');
                CREATE TABLE events_munich_202606 PARTITION OF events_munich
                    FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');
            ");

            // Hybrid-idempotency unique constraint (FR-002). Indexed
            // on (fab_id, event_id) so MQTT redelivery hits the
            // constraint at the leaf partition.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_events_fab_eventid
                    ON events (fab_id, event_id, ingested_at);
            ");

            // Per-source query path (FR-018: list filtered by source/
            // device/occurredAt). DESC matches the cursor-pagination
            // 'most recent first' default.
            migrationBuilder.Sql(@"
                CREATE INDEX ix_events_source_device_occurred
                    ON events (fab_id, source, device_id, occurred_at DESC);
            ");

            // Ingested-at index for the cursor itself.
            migrationBuilder.Sql(@"
                CREATE INDEX ix_events_ingested
                    ON events (fab_id, ingested_at DESC);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            // Dropping the parent cascades to every partition.
            migrationBuilder.Sql("DROP TABLE events CASCADE;");
        }
    }
}
