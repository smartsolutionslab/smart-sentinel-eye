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
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("integration_id")
            .HasConversion(id => id.Value, value => WebhookIntegrationIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(w => w.Name)
            .HasColumnName("name")
            .HasMaxLength(WebhookIntegrationName.MaximumLength)
            .HasConversion(n => n.Value, v => WebhookIntegrationName.From(v))
            .IsRequired();

        builder.Property(w => w.DefaultKind)
            .HasColumnName("default_kind")
            .HasMaxLength(Kind.MaximumLength)
            .HasConversion(k => k.Value, v => Kind.From(v))
            .IsRequired();

        builder.Property(w => w.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .HasConversion(h => h.Value, v => BearerTokenHash.FromStored(v))
            .IsRequired();

        builder.Property(w => w.RegisteredAt)
            .HasColumnName("registered_at")
            .IsRequired();

        builder.Property(w => w.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(w => w.ValidationMode)
            .HasColumnName("validation_mode")
            .HasConversion<int>()
            .HasDefaultValue(BearerValidationMode.StaticHash)
            .IsRequired();

        builder.Property(w => w.KeycloakClientId)
            .HasColumnName("keycloak_client_id")
            .HasMaxLength(255);

        builder.Property(w => w.RotatedAt)
            .HasColumnName("rotated_at");

        builder.Property(w => w.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.HasIndex(w => w.Name)
            .HasDatabaseName("ux_webhook_integrations_name")
            .IsUnique();

        builder.Ignore(w => w.PendingEvents);
    }
}
