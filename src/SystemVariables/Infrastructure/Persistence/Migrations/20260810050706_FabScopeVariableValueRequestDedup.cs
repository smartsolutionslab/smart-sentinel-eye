using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 014 T018: adds the fab to the dedup key.
    ///
    /// <para>
    /// The table is raw-SQL managed — no entity type, no model snapshot entry —
    /// so `dotnet ef` scaffolded this empty and the body is hand-written. It
    /// still follows the same four-step form as <c>FabScopeSystemVariables</c>,
    /// and for the same reason: the column is NOT NULL and the table can be
    /// populated, so adding it in one step would need a default that is not a
    /// valid fab.
    /// </para>
    ///
    /// <para>
    /// Why the key has to widen: two fabs' rules reacting to the same ingested
    /// event share a causing event identifier and a variable name. Keyed on
    /// that pair alone, the second fab's legitimate change is swallowed as a
    /// redelivery of the first — silently, because a dedup hit is a
    /// debug-level no-op. That is the normal case once both fabs run rules on
    /// one trigger, not an edge one.
    /// </para>
    ///
    /// <para>
    /// The backfill announces itself for the same reason the variables one
    /// does: "every row that exists belongs to munich" cannot be checked from
    /// inside a database whose old rows are exactly the ones with no fab. Here
    /// being wrong is milder and self-healing — a mis-attributed dedup row can
    /// at worst suppress one redelivery of one event, and the table is TTL'd
    /// to 7 days — so this warns and moves on.
    /// </para>
    /// </summary>
    public partial class FabScopeVariableValueRequestDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // 1 — nullable, so the column can be added to a populated table.
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    ADD COLUMN fab VARCHAR(32);
                """);

            // 2 — backfill, counting what it touched.
            migrationBuilder.Sql("""
                DO $$
                DECLARE attributed integer;
                BEGIN
                    UPDATE variable_value_request_dedup SET fab = 'munich' WHERE fab IS NULL;
                    GET DIAGNOSTICS attributed = ROW_COUNT;
                    IF attributed > 0 THEN
                        RAISE WARNING
                            'FabScopeVariableValueRequestDedup attributed % pre-existing dedup row(s) to fab ''munich''. At worst one redelivery per row is suppressed, and the table is TTL''d to 7 days.',
                            attributed;
                    END IF;
                END $$;
                """);

            // 3 — now the constraint can hold.
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    ALTER COLUMN fab SET NOT NULL;
                """);

            // 4 — swap the primary key. Both halves happen here: split across
            // two migrations there would be a released version with no
            // idempotency key on this table at all, which is worse than either
            // shape.
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    DROP CONSTRAINT variable_value_request_dedup_pkey;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    ADD PRIMARY KEY (fab, variable_name, causing_event_identifier);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Unlike the variables table, reverting here cannot fail on data
            // that outgrew the old shape: rows differing only by fab are
            // collapsed first. The loss is real but self-healing — dropping a
            // dedup row only un-suppresses a redelivery the handler would then
            // apply once more, and every row expires within 7 days anyway.
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    DROP CONSTRAINT variable_value_request_dedup_pkey;
                """);
            migrationBuilder.Sql("""
                DELETE FROM variable_value_request_dedup a
                    USING variable_value_request_dedup b
                    WHERE a.ctid > b.ctid
                      AND a.variable_name = b.variable_name
                      AND a.causing_event_identifier = b.causing_event_identifier;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    ADD PRIMARY KEY (variable_name, causing_event_identifier);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE variable_value_request_dedup
                    DROP COLUMN fab;
                """);
        }
    }
}
