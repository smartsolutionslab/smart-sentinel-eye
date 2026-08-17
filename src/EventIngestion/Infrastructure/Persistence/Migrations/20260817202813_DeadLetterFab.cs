using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives <c>dead_letters</c> the plant its delivery came from (spec 018
    /// FR-008), and backfills it from the address already stored (FR-013).
    ///
    /// <para>
    /// The column stays nullable. A delivery whose address does not name a
    /// plant has none, FR-010 forbids inventing one, and there is deliberately
    /// no follow-up NOT NULL migration to file — unlike #1467 for streams,
    /// where the null was transitional.
    /// </para>
    ///
    /// <para>
    /// The backfill guards on the four-segment <c>fab/a/b/c</c> shape
    /// <em>and</em> on the <see cref="Domain.Event.FabIdentifier"/> grammar, so
    /// it cannot write a value the domain rejects when it reads the row back —
    /// the defect spec 015 hit when a scaffolded <c>defaultValue: ""</c> left
    /// unparseable rows behind. A topic that fails either guard keeps its
    /// <c>NULL</c>, which is the correct answer rather than a fallback.
    /// </para>
    ///
    /// <para>
    /// No <c>RAISE WARNING</c> and no announced count, unlike specs 015 and
    /// 017. Those backfills guessed a fab and the warning flagged the guess;
    /// this one derives from data already present, so there is nothing to warn
    /// about. Its absence is the design.
    /// </para>
    /// </summary>
    public partial class DeadLetterFab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "fab",
                table: "dead_letters",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE dead_letters
                SET    fab = split_part(topic, '/', 2)
                WHERE  fab IS NULL
                  AND  split_part(topic, '/', 1) = 'fab'
                  AND  array_length(string_to_array(topic, '/'), 1) = 4
                  AND  split_part(topic, '/', 2) ~ '^[a-z][a-z0-9-]{1,31}$';
            ");

            migrationBuilder.CreateIndex(
                name: "ix_dead_letters_fab",
                table: "dead_letters",
                column: "fab");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Safe: a dead letter without a fab is exactly the state the system
            // is built to tolerate, so dropping the column loses an attribution
            // the backfill can re-derive, not data.
            migrationBuilder.DropIndex(
                name: "ix_dead_letters_fab",
                table: "dead_letters");

            migrationBuilder.DropColumn(
                name: "fab",
                table: "dead_letters");
        }
    }
}
