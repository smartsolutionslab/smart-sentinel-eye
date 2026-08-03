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
    /// <c>'munich'</c> is a literal rather than configuration: a migration
    /// must produce the same result on every environment, and a config-driven
    /// backfill would silently assign different fabs in dev and prod.
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
            migrationBuilder.Sql("UPDATE rules SET fab = 'munich' WHERE fab IS NULL;");

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
