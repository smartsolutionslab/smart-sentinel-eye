using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives a webhook integration the plant it belongs to (#1545, amending
    /// spec 018 FR-016).
    ///
    /// <para>
    /// Unlike <c>dead_letters.fab</c> this ends <c>NOT NULL</c>. A dead letter
    /// can honestly have no plant; an integration cannot — it is created by an
    /// operator, and an operator always has one. A null here would mean an
    /// integration whose deliveries can never be authorised, which is a broken
    /// row rather than a meaningful state.
    /// </para>
    ///
    /// <para>
    /// The scaffold arrived as <c>nullable: false, defaultValue: ""</c>, which
    /// is the defect spec 015 caught: <c>''</c> is not a legal
    /// <see cref="Domain.Event.FabIdentifier"/> (minimum length 2), so every
    /// pre-existing integration would have thrown on the next read. Replaced
    /// with add-nullable → backfill → tighten.
    /// </para>
    /// </summary>
    public partial class WebhookIntegrationFab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // 1 — nullable, so the column can be added to a populated table.
            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "webhook_integrations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // 2 — backfill. There is nothing on the row to derive a plant from,
            // unlike dead_letters where the topic carried it, so this guesses —
            // and the guess is 'munich', the only fab any integration could have
            // delivered into until this branch added dresden's partition.
            //
            // Warns rather than fails: refusing would block a deployment whose
            // integrations really are munich's, which is every deployment that
            // exists today. The warning names the count for the one case this
            // cannot detect — and it matters more here than in spec 015, because
            // a wrongly attributed integration does not merely become invisible,
            // it stops ingesting.
            migrationBuilder.Sql("""
                DO $$
                DECLARE attributed integer;
                BEGIN
                    UPDATE webhook_integrations SET fab = 'munich' WHERE fab IS NULL;
                    GET DIAGNOSTICS attributed = ROW_COUNT;
                    IF attributed > 0 THEN
                        RAISE WARNING
                            'WebhookIntegrationFab attributed % pre-existing webhook integration(s) to fab ''munich''. Any that belongs to another fab will now reject its own deliveries with 401 until its fab is corrected.',
                            attributed;
                    END IF;
                END $$;
                """);

            // 3 — now the constraint can hold.
            migrationBuilder.AlterColumn<string>(
                name: "fab",
                table: "webhook_integrations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_integrations_fab",
                table: "webhook_integrations",
                column: "fab");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Reversible without loss of anything that existed before: the
            // column's only content is this migration's own guess plus whatever
            // has been registered since.
            migrationBuilder.DropIndex(
                name: "ix_webhook_integrations_fab",
                table: "webhook_integrations");

            migrationBuilder.DropColumn(
                name: "fab",
                table: "webhook_integrations");
        }
    }
}
