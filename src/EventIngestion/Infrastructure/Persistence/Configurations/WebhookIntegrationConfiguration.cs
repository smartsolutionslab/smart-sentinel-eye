using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

public sealed class WebhookIntegrationConfiguration : IEntityTypeConfiguration<WebhookIntegration>
{
    public void Configure(EntityTypeBuilder<WebhookIntegration> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("webhook_integrations");
        builder.HasKey(integration => integration.Id);

        builder.Property(integration => integration.Id)
            .HasColumnName("integration_id")
            .HasConversion(id => id.Value, value => WebhookIntegrationIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(integration => integration.Name)
            .HasColumnName("name")
            .HasMaxLength(WebhookIntegrationName.MaximumLength)
            .HasConversion(name => name.Value, value => WebhookIntegrationName.From(value))
            .IsRequired();

        // NOT NULL, unlike dead_letters.fab: an integration is created by an
        // operator who always has a fab, so there is no honest null here (#1545).
        builder.Property(integration => integration.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(integration => integration.DefaultKind)
            .HasColumnName("default_kind")
            .HasMaxLength(Kind.MaximumLength)
            .HasConversion(kind => kind.Value, value => Kind.From(value))
            .IsRequired();

        builder.Property(integration => integration.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .HasConversion(hash => hash.Value, value => BearerTokenHash.FromStored(value))
            .IsRequired();

        builder.Property(integration => integration.RegisteredAt)
            .HasColumnName("registered_at")
            .HasConversion(v => v.Value, value => RegisteredAt.From(value))
            .IsRequired();

        builder.Property(integration => integration.RevokedAt)
            .HasColumnName("revoked_at")
            .HasConversion(v => v!.Value, value => RevokedAt.From(value));

        builder.Property(integration => integration.ValidationMode)
            .HasColumnName("validation_mode")
            .HasConversion<int>()
            .HasDefaultValue(BearerValidationMode.StaticHash)
            .IsRequired();

        builder.Property(integration => integration.KeycloakClientId)
            .HasColumnName("keycloak_client_id")
            .HasMaxLength(KeycloakClientIdentifier.MaximumLength)
            .HasConversion(client => client!.Value, value => KeycloakClientIdentifier.From(value));

        builder.Property(integration => integration.RotatedAt)
            .HasColumnName("rotated_at")
            .HasConversion(v => v!.Value, value => RotatedAt.From(value));

        builder.Property(integration => integration.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Still globally unique, not (fab, name): the name is the path segment
        // of POST /events/webhook/{name} and the ingest lookup has only the name
        // to resolve by. Making it per-fab would make that route ambiguous.
        builder.HasIndex(integration => integration.Name)
            .HasDatabaseName("ux_webhook_integrations_name")
            .IsUnique();

        builder.HasIndex(integration => integration.Fab)
            .HasDatabaseName("ix_webhook_integrations_fab");

        builder.Ignore(integration => integration.PendingEvents);
    }
}
