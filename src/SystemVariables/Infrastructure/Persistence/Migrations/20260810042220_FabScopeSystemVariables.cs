using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec 014: gives every system variable a fab.
    ///
    /// <para>
    /// Hand-corrected after scaffolding. <c>dotnet ef</c> generated a single
    /// <c>AddColumn(nullable: false, defaultValue: "")</c>, which on a
    /// populated table sets every existing variable's fab to the empty string —
    /// and <c>""</c> is not a valid <c>FabIdentifier</c> (minimum length 2,
    /// must start with a lowercase letter), so those variables would fail to
    /// materialise on the next read. The four-step form below is what makes
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
    /// The assumption that buys, stated plainly: <b>every variable that exists
    /// before this migration belongs to munich.</b> That holds because munich
    /// was the only fab when spec 013 and this feature landed. It does not hold
    /// for a database that predates them in some other fab — there, these
    /// variables would be attributed to a fab nobody operates, and every
    /// overlay referencing one would render its literal placeholder instead of
    /// a value. A fresh deployment is unaffected either way: the table is empty
    /// when this runs, so the backfill touches nothing.
    /// </para>
    ///
    /// <para>
    /// Since the assumption cannot be checked from inside the database — the
    /// old rows carry no fab, which is the entire point — the backfill counts
    /// what it changed and says so. On a fresh deployment that is silent; on a
    /// populated one it puts the assumption in the migration log at the moment
    /// it is applied, rather than leaving it to be discovered when screens go
    /// blank. Spec 013's equivalent fired for real when its quickstart was
    /// walked, naming four rules.
    /// </para>
    /// </summary>
    public partial class FabScopeSystemVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_system_variables_name_active",
                table: "system_variables");

            // 1 — nullable, so the column can be added to a populated table.
            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "system_variables",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // 2 — backfill. Variables defined before this feature belong to the
            // single live fab; leaving them unattributed would strand every
            // overlay that resolves one.
            //
            // Warns rather than fails: refusing would block a deployment whose
            // variables really are munich's, which is every deployment that
            // exists. The warning is for the one case this cannot detect.
            migrationBuilder.Sql("""
                DO $$
                DECLARE attributed integer;
                BEGIN
                    UPDATE system_variables SET fab = 'munich' WHERE fab IS NULL;
                    GET DIAGNOSTICS attributed = ROW_COUNT;
                    IF attributed > 0 THEN
                        RAISE WARNING
                            'FabScopeSystemVariables attributed % pre-existing system variable(s) to fab ''munich''. If this database belongs to another fab, overlays referencing them will now render the literal placeholder instead of a value.',
                            attributed;
                    END IF;
                END $$;
                """);

            // 3 — now the constraint can hold.
            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "system_variables",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            // 4 — the partial filter is carried over deliberately: archiving a
            // variable has always released its name for re-use, and scoping the
            // index to a fab must not quietly take that away.
            migrationBuilder.CreateIndex(
                name: "ux_system_variables_fab_name_active",
                table: "system_variables",
                columns: new[] { "fab", "name" },
                unique: true,
                filter: "state <> 'Archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_system_variables_fab_name_active",
                table: "system_variables");

            // Dropping the column discards which fab each variable belonged to,
            // and rolling forward again re-attributes every one of them to
            // munich. That is unrecoverable from inside the database, so a
            // rollback after variables have been defined across fabs wants a
            // dump taken first. The index conflict below is the louder failure;
            // this is the quieter and worse one.
            migrationBuilder.DropColumn(
                name: "fab",
                table: "system_variables");

            // Reverting can fail where the forward migration succeeded: two
            // fabs may each hold a live variable of the same name, which the
            // name-only unique index cannot represent. That is correct — the
            // data genuinely does not fit the old shape, and silently
            // discarding one of the two variables would be worse.
            migrationBuilder.CreateIndex(
                name: "ux_system_variables_name_active",
                table: "system_variables",
                column: "name",
                unique: true,
                filter: "state <> 'Archived'");
        }
    }
}
