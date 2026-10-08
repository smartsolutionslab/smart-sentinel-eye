using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

public sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetter>
{
    public void Configure(EntityTypeBuilder<DeadLetter> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("dead_letters");
        builder.HasKey(deadLetter => deadLetter.Id);

        builder.Property(deadLetter => deadLetter.Id)
            .HasColumnName("dead_letter_id")
            .HasConversion(id => id.Value, value => DeadLetterIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(deadLetter => deadLetter.Topic)
            .HasColumnName("topic")
            .HasMaxLength(DeliveryTopic.MaximumLength)
            .HasConversion(topic => topic.Value, value => DeliveryTopic.From(value))
            .IsRequired();

        // Nullable permanently (spec 018 FR-010): a delivery whose address does
        // not name a plant has none, and no later migration tightens this.
        builder.Property(deadLetter => deadLetter.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            // `fab!` is safe: EF does not invoke a converter for a null value,
            // so the lambda only ever sees an attributed row.
            .HasConversion(fab => fab!.Value, value => FabIdentifier.From(value))
            .IsRequired(false);

        builder.Property(deadLetter => deadLetter.RawPayload)
            .HasColumnName("raw_payload")
            .HasColumnType("text")
            .HasConversion(payload => payload.Value, value => RawPayload.From(value))
            .IsRequired();

        builder.Property(deadLetter => deadLetter.Error)
            .HasColumnName("error")
            .HasMaxLength(RejectionReason.MaximumLength)
            .HasConversion(reason => reason.Value, value => RejectionReason.From(value))
            .IsRequired();

        builder.Property(deadLetter => deadLetter.RejectedAt)
            .HasColumnName("rejected_at")
            .HasConversion(v => v.Value, value => RejectedAt.From(value))
            .IsRequired();

        // Spec 317 (#2325), FR-001. Required — every row has a reason, back-filled
        // by the migration for rows captured before this spec.
        builder.Property(deadLetter => deadLetter.Reason)
            .HasColumnName("reason")
            .HasConversion(reason => reason.Value, value => DeadLetterReason.From(value))
            .IsRequired();

        // Nullable (FR-002): a parse failure never reached a parsed envelope, so
        // it has no kind. `kind!` is safe for the same reason `fab!` is above —
        // EF never invokes the converter for a null value.
        builder.Property(deadLetter => deadLetter.Kind)
            .HasColumnName("kind")
            .HasMaxLength(Kind.MaximumLength)
            .HasConversion(kind => kind!.Value, value => Kind.From(value))
            .IsRequired(false);

        // Spec 317 FR-003. Defaults to 'Held' at the database level too, so a
        // row inserted outside this mapping (the migration's back-fill) still
        // satisfies the column's NOT NULL.
        builder.Property(deadLetter => deadLetter.State)
            .HasColumnName("state")
            .HasConversion(state => state.Value, value => HoldState.From(value))
            .HasDefaultValue(HoldState.Held)
            .IsRequired();

        builder.Property(deadLetter => deadLetter.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(deadLetter => deadLetter.RejectedAt)
            .HasDatabaseName("ix_dead_letters_rejected_at");

        // Plain, not composite: the listing filters on fab and orders by
        // rejected_at, and the table is small enough that the two indexes
        // separately are the honest shape.
        builder.HasIndex(deadLetter => deadLetter.Fab)
            .HasDatabaseName("ix_dead_letters_fab");

        // Spec 317 FR-004. Serves the quarantine listing (?reason=&state=) and
        // FR-007's promotion UPDATE's WHERE clause. ix_dead_letters_fab stays —
        // it is a prefix of this one, and dropping it is a separate, measured
        // clean-up (plan.md §4).
        builder.HasIndex(deadLetter => new { deadLetter.Fab, deadLetter.Reason, deadLetter.State })
            .HasDatabaseName("ix_dead_letters_fab_reason_state");

        builder.Ignore(deadLetter => deadLetter.PendingEvents);
    }
}
