using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="RegisteredClientAggregate"/>.
/// The <c>ClientSecret</c> domain VO is NOT persisted — Keycloak
/// is the system of record for credentials; this table is the
/// local audit + dedup mirror.
/// </summary>
public sealed class RegisteredClientConfiguration : IEntityTypeConfiguration<RegisteredClientAggregate>
{
    public void Configure(EntityTypeBuilder<RegisteredClientAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("registered_clients");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("registered_client_id")
            .HasConversion(id => id.Value, value => RegisteredClientIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(r => r.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(ClientId.MaximumLength)
            .HasConversion(c => c.Value, v => ClientId.From(v))
            .IsRequired();

        builder.Property(r => r.Kind)
            .HasColumnName("kind")
            .HasMaxLength(32)
            .HasConversion(k => k.Value, v => ClientKind.From(v))
            .IsRequired();

        builder.Property(r => r.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(f => f.Value, v => FabIdentifier.From(v))
            .IsRequired();

        builder.Property(r => r.RegisteredAt)
            .HasColumnName("registered_at")
            .IsRequired();

        builder.Property(r => r.RegisteredBy)
            .HasColumnName("registered_by")
            .HasConversion(o => o.Value, v => OperatorIdentifier.From(v))
            .IsRequired();

        builder.Property(r => r.DisabledAt).HasColumnName("disabled_at");
        builder.Property(r => r.LastRotatedAt).HasColumnName("last_rotated_at");

        builder.Property(r => r.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Active row uniqueness on client_id (FR-002 mirrors
        // spec 005's archived-name pattern: disabled rows release
        // the clientId for re-registration).
        builder.HasIndex(r => r.ClientId)
            .HasDatabaseName("ux_registered_clients_clientid_active")
            .IsUnique()
            .HasFilter("disabled_at IS NULL");

        // Lookup index for the management UI / spec 009 audit queries.
        builder.HasIndex(r => new { r.Kind, r.Fab, r.DisabledAt })
            .HasDatabaseName("ix_registered_clients_kind_fab_disabled");

        builder.Ignore(r => r.PendingEvents);
    }
}
