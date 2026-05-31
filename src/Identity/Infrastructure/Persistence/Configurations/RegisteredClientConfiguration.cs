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
        builder.HasKey(client => client.Id);

        builder.Property(client => client.Id)
            .HasColumnName("registered_client_id")
            .HasConversion(id => id.Value, value => RegisteredClientIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(client => client.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(ClientId.MaximumLength)
            .HasConversion(clientId => clientId.Value, value => ClientId.From(value))
            .IsRequired();

        builder.Property(client => client.Kind)
            .HasColumnName("kind")
            .HasMaxLength(32)
            .HasConversion(kind => kind.Value, value => ClientKind.From(value))
            .IsRequired();

        builder.Property(client => client.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(client => client.RegisteredAt)
            .HasColumnName("registered_at")
            .IsRequired();

        builder.Property(client => client.RegisteredBy)
            .HasColumnName("registered_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(client => client.DisabledAt).HasColumnName("disabled_at");
        builder.Property(client => client.LastRotatedAt).HasColumnName("last_rotated_at");

        builder.Property(client => client.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Active row uniqueness on client_id (FR-002 mirrors
        // spec 005's archived-name pattern: disabled rows release
        // the clientId for re-registration).
        builder.HasIndex(client => client.ClientId)
            .HasDatabaseName("ux_registered_clients_clientid_active")
            .IsUnique()
            .HasFilter("disabled_at IS NULL");

        // Lookup index for the management UI / spec 009 audit queries.
        builder.HasIndex(client => new { client.Kind, client.Fab, client.DisabledAt })
            .HasDatabaseName("ix_registered_clients_kind_fab_disabled");

        builder.Ignore(client => client.PendingEvents);
    }
}
