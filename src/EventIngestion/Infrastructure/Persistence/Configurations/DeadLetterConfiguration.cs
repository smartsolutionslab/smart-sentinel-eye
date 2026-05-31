using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

public sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetter>
{
    public void Configure(EntityTypeBuilder<DeadLetter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("dead_letters");
        builder.HasKey(deadLetter => deadLetter.Id);

        builder.Property(deadLetter => deadLetter.Id)
            .HasColumnName("dead_letter_id")
            .HasConversion(id => id.Value, value => DeadLetterIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(deadLetter => deadLetter.Topic)
            .HasColumnName("topic")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(deadLetter => deadLetter.RawPayload)
            .HasColumnName("raw_payload")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(deadLetter => deadLetter.Error)
            .HasColumnName("error")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(deadLetter => deadLetter.RejectedAt)
            .HasColumnName("rejected_at")
            .IsRequired();

        builder.Property(deadLetter => deadLetter.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.HasIndex(deadLetter => deadLetter.RejectedAt)
            .HasDatabaseName("ix_dead_letters_rejected_at");

        builder.Ignore(deadLetter => deadLetter.PendingEvents);
    }
}
