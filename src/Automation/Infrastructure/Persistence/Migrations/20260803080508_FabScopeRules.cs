using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.Automation.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 013: gives every rule a fab.
    ///
    /// <para>
    /// Hand-corrected after scaffolding. <c>dotnet ef</c> generated a single
    /// <c>AddColumn(nullable: false, defaultValue: "")</c>, which on a
    /// populated table sets every existing rule's fab to the empty string —
    /// and <c>""</c> is not a valid <c>FabIdentifier</c> (minimum length 2,
    /// must start with a lowercase letter), so those rules would fail to
    /// materialise on the next read. The three-step form below is what makes
    /// this safe against live data.
    /// </para>
    ///
    /// <para>
    /// <c>'munich'</c> is a literal rather than configuration: a migration must
    /// produce the same result on every environment, and a config-driven
    /// backfill would silently assign different fabs in dev and prod.
    /// </para>
    ///
    /// <para>
    /// The assumption that buys, stated plainly: <b>every rule that exists
    /// before this migration belongs to munich.</b> That holds because munich
    /// was the only fab when spec 013 landed. It does not hold for a database
    /// that predates spec 013 in some other fab — there, these rules would be
    /// attributed to a fab nobody operates, and would simply stop firing. A
    /// fresh deployment is unaffected either way: the table is empty when this
    /// runs, so the backfill touches nothing.
    /// </para>
    ///
    /// <para>
    /// Since the assumption cannot be checked from inside the database — the
    /// old rows carry no fab, which is the entire point — the backfill counts
    /// what it changed and says so. On a fresh deployment that is silent; on a
    /// populated one it puts the assumption in the migration log at the moment
    /// it is applied, rather than leaving it to be discovered when rules stop
    /// firing.
    /// </para>
    ///
    /// <para>
    /// Both index swaps happen inside this one migration. Split across two,
    /// there would be a released version in which no uniqueness constraint on
    /// rule names existed at all.
    /// </para>
    /// </summary>
    public partial class FabScopeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rules_trigger_state",
                table: "rules");

            migrationBuilder.DropIndex(
                name: "ux_rules_name_active",
                table: "rules");

            // 1 — nullable, so the column can be added to a populated table.
            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "rules",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // 2 — backfill. Rules authored before this feature belong to the
            // single live fab; archiving them instead would have stopped
            // automation that is currently running (spec 013 Assumptions).
            //
            // Warns rather than fails: refusing would block a deployment whose
            // rules really are munich's, which is every deployment that exists.
            // The warning is for the one case this cannot detect.
            migrationBuilder.Sql("""
                DO $$
                DECLARE attributed integer;
                BEGIN
                    UPDATE rules SET fab = 'munich' WHERE fab IS NULL;
                    GET DIAGNOSTICS attributed = ROW_COUNT;
                    IF attributed > 0 THEN
                        RAISE WARNING
                            'FabScopeRules attributed % pre-existing rule(s) to fab ''munich''. If this database belongs to another fab, those rules now match no event and will not fire.',
                            attributed;
                    END IF;
                END $$;
                """);

            // 3 — now the constraint can hold.
            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "rules",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_rules_fab_trigger_state",
                table: "rules",
                columns: new[] { "fab", "trigger_source", "trigger_kind", "state" });

            // The partial filter is carried over deliberately: archiving a
            // rule has always released its name for re-use, and scoping the
            // index to a fab must not quietly take that away.
            migrationBuilder.CreateIndex(
                name: "ux_rules_fab_name_active",
                table: "rules",
                columns: new[] { "fab", "name" },
                unique: true,
                filter: "state <> 'Archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rules_fab_trigger_state",
                table: "rules");

            migrationBuilder.DropIndex(
                name: "ux_rules_fab_name_active",
                table: "rules");

            // Dropping the column discards which fab each rule belonged to,
            // and rolling forward again re-backfills every one of them to
            // munich. That is unrecoverable from inside the database, so a
            // rollback after rules have been authored across fabs wants a dump
            // taken first. The index conflict below is the louder failure; this
            // is the quieter and worse one.
            migrationBuilder.DropColumn(
                name: "fab",
                table: "rules");

            migrationBuilder.CreateIndex(
                name: "ix_rules_trigger_state",
                table: "rules",
                columns: new[] { "trigger_source", "trigger_kind", "state" });

            // Reverting can fail where the forward migration succeeded: two
            // fabs may each hold a live rule of the same name, which the
            // name-only unique index cannot represent. That is correct — the
            // data genuinely does not fit the old shape, and silently
            // discarding one of the two rules would be worse.
            migrationBuilder.CreateIndex(
                name: "ux_rules_name_active",
                table: "rules",
                column: "name",
                unique: true,
                filter: "state <> 'Archived'");
        }
    }
}
