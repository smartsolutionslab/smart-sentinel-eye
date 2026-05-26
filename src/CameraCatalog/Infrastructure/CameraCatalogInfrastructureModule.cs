using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.CameraCatalog.Application.EventHandlers;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure;

/// <summary>
/// Composition root for the Camera Catalog Infrastructure layer (ADR-0051).
///
/// Two entry points:
/// - <see cref="AddCameraCatalogPersistence"/> registers only what the
///   MigrationRunner needs: <c>DbContext</c> + <see cref="IMigrator"/>.
///   No Wolverine, no RabbitMQ, no repositories.
/// - <see cref="AddCameraCatalogInfrastructure"/> layers the full stack
///   (Wolverine + outbox + RabbitMQ + repositories + event handlers) on
///   top, for the API process.
/// </summary>
public static class CameraCatalogInfrastructureModule
{
    public const string ContextName = "camera-catalog";
    public const string OutboxSchema = "wolverine_camera_catalog";
    public const string DatabaseConnectionName = "camera-catalog-db";

    public static IHostApplicationBuilder AddCameraCatalogPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<CameraCatalogDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, CameraCatalogMigrator>();

        return builder;
    }

    public static IHostApplicationBuilder AddCameraCatalogInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCameraCatalogPersistence();

        builder.Services.AddScoped<ICameraRepository, CameraRepository>();
        builder.Services.AddScoped<ICameraQuerySource, CameraQuerySource>();
        builder.Services.AddScoped<
            IDomainEventHandler<Domain.Camera.Events.CameraRegisteredDomainEvent>,
            CameraRegisteredDomainEventHandler>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        builder.AddWolverineForContext<CameraCatalogDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: DatabaseConnectionName);

        return builder;
    }
}
