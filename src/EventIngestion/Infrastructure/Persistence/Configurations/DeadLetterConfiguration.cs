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
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("dead_letter_id")
            .HasConversion(id => id.Value, value => DeadLetterIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(d => d.Topic)
            .HasColumnName("topic")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(d => d.RawPayload)
            .HasColumnName("raw_payload")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(d => d.Error)
            .HasColumnName("error")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(d => d.RejectedAt)
            .HasColumnName("rejected_at")
            .IsRequired();

        builder.Property(d => d.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.HasIndex(d => d.RejectedAt)
            .HasDatabaseName("ix_dead_letters_rejected_at");

        builder.Ignore(d => d.PendingEvents);
    }
}
