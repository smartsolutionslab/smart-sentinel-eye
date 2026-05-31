using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

public sealed class WebhookIntegrationConfiguration : IEntityTypeConfiguration<WebhookIntegration>
{
    public void Configure(EntityTypeBuilder<WebhookIntegration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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
            .IsRequired();

        builder.Property(integration => integration.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(integration => integration.ValidationMode)
            .HasColumnName("validation_mode")
            .HasConversion<int>()
            .HasDefaultValue(BearerValidationMode.StaticHash)
            .IsRequired();

        builder.Property(integration => integration.KeycloakClientId)
            .HasColumnName("keycloak_client_id")
            .HasMaxLength(255);

        builder.Property(integration => integration.RotatedAt)
            .HasColumnName("rotated_at");

        builder.Property(integration => integration.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.HasIndex(integration => integration.Name)
            .HasDatabaseName("ux_webhook_integrations_name")
            .IsUnique();

        builder.Ignore(integration => integration.PendingEvents);
    }
}
