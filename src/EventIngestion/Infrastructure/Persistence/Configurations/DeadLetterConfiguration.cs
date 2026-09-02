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

        builder.Property(deadLetter => deadLetter.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken();

        builder.HasIndex(deadLetter => deadLetter.RejectedAt)
            .HasDatabaseName("ix_dead_letters_rejected_at");

        // Plain, not composite: the listing filters on fab and orders by
        // rejected_at, and the table is small enough that the two indexes
        // separately are the honest shape.
        builder.HasIndex(deadLetter => deadLetter.Fab)
            .HasDatabaseName("ix_dead_letters_fab");

        builder.Ignore(deadLetter => deadLetter.PendingEvents);
    }
}
