using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="RegisteredEventType"/> aggregate (spec
/// 143). Name uniqueness is per fab and enforced twice (FR-004): the
/// application-level lookup, and the partial unique index below — the same
/// shape as <c>ux_system_variables_fab_name_active</c>
/// (<c>VariableConfiguration.cs:116</c>).
/// </summary>
public sealed class RegisteredEventTypeConfiguration : IEntityTypeConfiguration<RegisteredEventType>
{
    public void Configure(EntityTypeBuilder<RegisteredEventType> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("registered_event_types");
        builder.HasKey(eventType => eventType.Id);

        builder.Property(eventType => eventType.Id)
            .HasColumnName("registered_event_type_id")
            .HasConversion(id => id.Value, value => RegisteredEventTypeIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(eventType => eventType.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(eventType => eventType.Kind)
            .HasColumnName("kind")
            .HasMaxLength(Kind.MaximumLength)
            .HasConversion(kind => kind.Value, value => Kind.From(value))
            .IsRequired();

        builder.Property(eventType => eventType.State)
            .HasColumnName("state")
            .HasMaxLength(16)
            .HasConversion(state => state.Value, value => RegistrationState.From(value))
            .IsRequired();

        // Owned reference onto registered_at + registered_by. Navigation(...)
        // .IsRequired() keeps them NOT NULL; without it the model silently
        // diverges from the schema (VariableConfiguration.cs:86,98; #2022).
        builder.OwnsOne(eventType => eventType.Registration, registration =>
        {
            registration.Property(value => value.RegisteredAt)
                .HasColumnName("registered_at")
                .HasConversion(at => at.Value, value => RegisteredAt.From(value))
                .IsRequired();

            registration.Property(value => value.RegisteredBy)
                .HasColumnName("registered_by")
                .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
                .IsRequired();
        });
        builder.Navigation(eventType => eventType.Registration).IsRequired();

        builder.Property(eventType => eventType.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken()
            .IsRequired();

        // FR-004's second enforcement, raw SQL against the column name and the
        // stored value (plan.md R4) — copy of VariableConfiguration.cs:116.
        // Partial so retiring releases the name for re-use (FR-004).
        builder.HasIndex(eventType => new { eventType.Fab, eventType.Kind })
            .HasDatabaseName("ux_registered_event_types_fab_kind")
            .IsUnique()
            .HasFilter("state <> 'Retired'");

        builder.HasIndex(eventType => eventType.Fab)
            .HasDatabaseName("ix_registered_event_types_fab");

        builder.Ignore(eventType => eventType.PendingEvents);
    }
}
