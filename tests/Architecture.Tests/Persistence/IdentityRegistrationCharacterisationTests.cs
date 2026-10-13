using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 348, ADR-0144), captured green on unmodified
/// <c>develop</c> (<c>ed702aa5</c>) before <c>AddIdentityInfrastructure</c> is
/// split into private registration groups. The refactor must not change a
/// lifetime, an implementation or the order registrations are made in: the
/// container's last-wins resolution makes order observable (notably the
/// <c>Replace</c> of <see cref="IRevokedClientSource"/>, which Identity alone
/// must not take from the default), and nothing else asserts it.
///
/// <para>
/// <b>What is pinned.</b> Every descriptor whose service or implementation type
/// mentions a <c>SmartSentinelEye.Identity.*</c> type (recursively through
/// generic arguments), plus the shared services the module registers from
/// foreign assemblies. EF Core's, Wolverine's and JasperFx's own plumbing is
/// left out, as is any total count: both move with package bumps and say
/// nothing about this module.
/// </para>
///
/// <para>
/// Counterfactual this file was seen to catch: swapping two registrations,
/// changing a lifetime, or dropping the <c>Replace</c> line each fails
/// <see cref="The_identity_registrations_are_these_in_this_order"/>.
/// </para>
/// </summary>
public class IdentityRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.Identity.";

    private static readonly Type[] SharedServicesTheModuleRegisters =
    [
        typeof(IClock),
        typeof(IDomainEventDispatcher),
        typeof(TimeProvider),
        typeof(IHttpContextAccessor),
        typeof(IIdempotencyStore),
        typeof(IRevokedClientSource),
    ];

    private static readonly string[] FrameworkNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "JasperFx",
        "Wolverine",
    ];

    private static readonly string[] ExpectedRegistrations =
    [
        "Scoped IdentityDbContext -> factory",
        "Singleton IMigrator -> IdentityMigrator",
        "Scoped IRegisteredClientRepository -> RegisteredClientRepository",
        "Scoped IRegisteredClientQuerySource -> RegisteredClientQuerySource",
        "Scoped IDomainEventDispatcher -> DomainEventDispatcher",
        "Singleton IClock -> SystemClock",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Singleton IOptionsChangeTokenSource<KeycloakAdminOptions> -> instance of ConfigurationChangeTokenSource<KeycloakAdminOptions>",
        "Singleton IConfigureOptions<KeycloakAdminOptions> -> instance of NamedConfigureFromConfigurationOptions<KeycloakAdminOptions>",
        "Transient IConfigureOptions<KeycloakAdminOptions> -> factory",
        "Transient KeycloakAdminAuthorizationHandler -> KeycloakAdminAuthorizationHandler",
        "Singleton KeycloakAdminTokenProvider -> KeycloakAdminTokenProvider",
        "Transient HttpKeycloakAdminClient -> factory",
        "Scoped IKeycloakAdminClient -> factory",
        "Singleton IHttpContextAccessor -> HttpContextAccessor",
        "Transient CallerTokenForwardingHandler -> CallerTokenForwardingHandler",
        "Transient IWebhookIntegrationStatusLookup -> factory",
        "Scoped KioskPrivilegeSweep -> KioskPrivilegeSweep",
        "Singleton IHostedService -> KioskPrivilegeSweepHostedService",
        "Scoped OrphanedClientSweep -> OrphanedClientSweep",
        "Singleton IHostedService -> OrphanedClientSweepHostedService",
        "Scoped IDomainEventHandler<ClientRegisteredDomainEvent> -> ClientRegisteredDomainEventHandler",
        "Scoped EnrollKioskCommandHandler -> EnrollKioskCommandHandler",
        "Scoped ICommandHandler<EnrollKioskCommand, Result<KioskCredentialsDto, EnrollKioskError>> -> EnrollKioskCommandHandler",
        "Scoped DisableKioskCommandHandler -> DisableKioskCommandHandler",
        "Scoped ICommandHandler<DisableKioskCommand, Result<RegisteredClientIdentifier, DisableKioskError>> -> DisableKioskCommandHandler",
        "Scoped RegisterDeviceCommandHandler -> RegisterDeviceCommandHandler",
        "Scoped ICommandHandler<RegisterDeviceCommand, Result<DeviceCredentialsDto, RegisterDeviceError>> -> RegisterDeviceCommandHandler",
        "Scoped DisableDeviceCommandHandler -> DisableDeviceCommandHandler",
        "Scoped ICommandHandler<DisableDeviceCommand, Result<RegisteredClientIdentifier, DisableDeviceError>> -> DisableDeviceCommandHandler",
        "Scoped DisableWebhookClientCommandHandler -> DisableWebhookClientCommandHandler",
        "Scoped ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> -> DisableWebhookClientCommandHandler",
        "Scoped RotateWebhookClientCommandHandler -> RotateWebhookClientCommandHandler",
        "Scoped ICommandHandler<RotateWebhookClientCommand, Result<WebhookClientCredentialsDto, RotateWebhookClientError>> -> RotateWebhookClientCommandHandler",
        "Scoped ListDevicesQueryHandler -> ListDevicesQueryHandler",
        "Scoped IIdempotencyStore -> IdempotencyStore<IdentityDbContext>",
        "Singleton IHostedService -> IdempotencyReservationSweepHostedService<IdentityDbContext>",
        "Scoped ReplayRegisteredClientQueryHandler -> ReplayRegisteredClientQueryHandler",
        "Scoped IQueryHandler<ListDevicesQuery, Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>> -> ListDevicesQueryHandler",
        "Scoped ListKiosksQueryHandler -> ListKiosksQueryHandler",
        "Scoped IQueryHandler<ListKiosksQuery, Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>> -> ListKiosksQueryHandler",
        "Scoped ListWebhookClientsQueryHandler -> ListWebhookClientsQueryHandler",
        "Scoped IQueryHandler<ListWebhookClientsQuery, Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>> -> ListWebhookClientsQueryHandler",
        "Scoped ListRevokedClientsQueryHandler -> ListRevokedClientsQueryHandler",
        "Scoped IQueryHandler<ListRevokedClientsQuery, Result<IReadOnlyList<RevokedClientEntry>, ListClientsError>> -> ListRevokedClientsQueryHandler",
        "Singleton IRevokedClientSource -> LocalRevokedClientSource",
        "Singleton IConfigureOptions<KeycloakAdminOptions> -> factory",
        "Scoped IEventBus -> OutboxEventBus<IdentityDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<IdentityDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "identity",
            builder => captured = [.. builder.Services]);
        return [.. captured];
    }

    private static bool MentionsContext(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Assembly.GetName().Name?.StartsWith(ContextAssemblyPrefix, StringComparison.Ordinal) == true)
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(MentionsContext);
    }

    private static bool IsFrameworkPlumbing(Type serviceType) =>
        FrameworkNamespacePrefixes.Any(
            prefix => serviceType.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true);

    private static bool IsPinned(ServiceDescriptor descriptor)
    {
        if (SharedServicesTheModuleRegisters.Contains(descriptor.ServiceType))
        {
            return true;
        }

        if (IsFrameworkPlumbing(descriptor.ServiceType))
        {
            return false;
        }

        return MentionsContext(descriptor.ServiceType) || MentionsContext(descriptor.ImplementationType);
    }

    private static string Name(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var arguments = string.Join(", ", type.GetGenericArguments().Select(Name));
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{arguments}>";
    }

    private static string Implementation(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType is not null)
        {
            return Name(descriptor.ImplementationType);
        }

        if (descriptor.ImplementationInstance is not null)
        {
            return $"instance of {Name(descriptor.ImplementationInstance.GetType())}";
        }

        return "factory";
    }

    private static string Render(ServiceDescriptor descriptor) =>
        $"{descriptor.Lifetime} {Name(descriptor.ServiceType)} -> {Implementation(descriptor)}";

    [Fact]
    public void The_identity_registrations_are_these_in_this_order()
    {
        var registrations = CaptureComposedRegistrations()
            .Where(IsPinned)
            .Select(Render)
            .ToArray();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_revoked_client_source_is_the_local_singleton_and_the_only_one()
    {
        var sources = CaptureComposedRegistrations()
            .Where(descriptor => descriptor.ServiceType == typeof(IRevokedClientSource))
            .Select(Render)
            .ToArray();

        sources.ShouldBe(["Singleton IRevokedClientSource -> LocalRevokedClientSource"]);
    }

    [Fact]
    public void The_db_context_factory_is_a_singleton_and_the_db_context_is_scoped()
    {
        var registrations = CaptureComposedRegistrations();

        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<SmartSentinelEye.Identity.Infrastructure.Persistence.IdentityDbContext>))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(SmartSentinelEye.Identity.Infrastructure.Persistence.IdentityDbContext))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
