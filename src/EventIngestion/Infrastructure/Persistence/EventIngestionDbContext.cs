using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the EventIngestion bounded context. Owns the
/// partitioned <c>events</c> table, the <c>webhook_integrations</c>
/// table, the <c>dead_letters</c> audit table (spec 006), the
/// <c>registered_event_types</c> registry (spec 143), and the
/// <c>source_modes</c> per-source admission policy (spec 269).
/// Wolverine outbox tables live in a sibling schema configured by
/// <c>AddWolverineForContext</c> (ADR-0088).
/// </summary>
public sealed class EventIngestionDbContext(DbContextOptions<EventIngestionDbContext> options)
    : DbContext(options)
{
    public DbSet<EventAggregate> Events => Set<EventAggregate>();

    public DbSet<WebhookIntegration> WebhookIntegrations => Set<WebhookIntegration>();

    public DbSet<DeadLetter> DeadLetters => Set<DeadLetter>();

    public DbSet<RegisteredEventType> RegisteredEventTypes => Set<RegisteredEventType>();

    public DbSet<SourceMode> SourceModes => Set<SourceMode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Ensure.That(modelBuilder).IsNotNull();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventIngestionDbContext).Assembly);
    }
}
