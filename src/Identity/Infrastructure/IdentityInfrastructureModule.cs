using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.RegisteredClient.Events;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure;

/// <summary>
/// Composition root for the Identity Infrastructure layer
/// (ADR-0051). Wires persistence, Keycloak admin REST client,
/// command handlers, and the Wolverine outbox.
/// </summary>
public static class IdentityInfrastructureModule
{
    public const string ContextName = "identity";
    public const string OutboxSchema = "wolverine_identity";
    public const string KeycloakResourceName = "keycloak";

    public static IHostApplicationBuilder AddIdentityInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddIdentityPersistence();

        builder.Services.AddScoped<IRegisteredClientRepository, RegisteredClientRepository>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();
        builder.Services.AddSingleton(TimeProvider.System);

        // Keycloak admin REST client. The base URL comes from the
        // Aspire-injected ConnectionStrings:keycloak; the admin
        // client_id + secret are configuration values.
        builder.Services.AddOptions<KeycloakAdminOptions>()
            .Bind(builder.Configuration.GetSection(KeycloakAdminOptions.SectionName))
            .Configure<IConfiguration>((opts, config) =>
            {
                opts.BaseUrl = config.GetConnectionString(KeycloakResourceName)
                    ?? config[$"services:{KeycloakResourceName}:http:0"]
                    ?? config[$"services:{KeycloakResourceName}:https:0"]
                    ?? throw new InvalidOperationException(
                        "Keycloak base URL not found; expected ConnectionStrings:keycloak or services:keycloak:*.");
            });

        builder.Services.AddHttpClient<KeycloakAdminTokenProvider>(ConfigureHttpClient);
        builder.Services.AddHttpClient<HttpKeycloakAdminClient>(ConfigureHttpClient);
        builder.Services.AddScoped<IKeycloakAdminClient>(sp => sp.GetRequiredService<HttpKeycloakAdminClient>());

        // Domain event handler — fans out DeviceRegisteredV1 /
        // KioskEnrolledV1.
        builder.Services.AddScoped<
            IDomainEventHandler<ClientRegisteredDomainEvent>,
            ClientRegisteredDomainEventHandler>();

        // Hand-rolled command handler registrations.
        builder.Services.AddScoped<EnrollKioskCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<EnrollKioskCommand, Result<KioskCredentialsDto, EnrollKioskError>>,
            EnrollKioskCommandHandler>();
        builder.Services.AddScoped<DisableKioskCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<DisableKioskCommand, Result<RegisteredClientIdentifier, DisableKioskError>>,
            DisableKioskCommandHandler>();
        builder.Services.AddScoped<RegisterDeviceCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<RegisterDeviceCommand, Result<DeviceCredentialsDto, RegisterDeviceError>>,
            RegisterDeviceCommandHandler>();
        builder.Services.AddScoped<DisableDeviceCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<DisableDeviceCommand, Result<RegisteredClientIdentifier, DisableDeviceError>>,
            DisableDeviceCommandHandler>();
        builder.Services.AddScoped<RotateWebhookClientCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<
                RotateWebhookClientCommand,
                Result<WebhookClientCredentialsDto, RotateWebhookClientError>>,
            RotateWebhookClientCommandHandler>();

        builder.AddWolverineForContext<IdentityDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: IdentityPersistenceModule.DatabaseConnectionName);

        return builder;
    }

    private static void ConfigureHttpClient(IServiceProvider sp, HttpClient client)
    {
        KeycloakAdminOptions opts = sp.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<KeycloakAdminOptions>>().Value;
#pragma warning disable S1075
        string baseUrl = opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + '/';
#pragma warning restore S1075
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    }

}
