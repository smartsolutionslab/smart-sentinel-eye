using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 346, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AddEventIngestionInfrastructure</c> is split into
/// private registration groups. The refactor must not change a lifetime, an
/// implementation or the order registrations are made in: the container's
/// last-wins resolution and the hosted services' start order make order
/// observable, and nothing else asserts it.
///
/// <para>
/// <b>What is pinned.</b> The registrations this context makes, in order: every
/// descriptor whose service type, implementation type or implementation factory
/// is declared in a <c>SmartSentinelEye.EventIngestion.*</c> assembly (recursively
/// through generic arguments), plus the shared services the module registers
/// that live in foreign assemblies (<see cref="IClock"/>,
/// <see cref="IDomainEventDispatcher"/>, <see cref="TimeProvider"/>). EF Core's,
/// Wolverine's and JasperFx's own plumbing is left out, as is any total count:
/// both move with package bumps and say nothing about this module. A factory
/// renders as <c>factory</c>, never as its compiler-generated name, which moves
/// when the lambda moves to another method.
/// </para>
/// </summary>
public class EventIngestionRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.EventIngestion.";

    private static readonly Type[] SharedServicesTheModuleRegisters =
    [
        typeof(IClock),
        typeof(IDomainEventDispatcher),
        typeof(TimeProvider),
    ];

    private static readonly string[] FrameworkNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "JasperFx",
        "Wolverine",
    ];

    private static readonly string[] ExpectedRegistrations =
    [
        "Scoped EventIngestionDbContext -> factory",
        "Singleton IMigrator -> EventIngestionMigrator",
        "Singleton FabPartitionProvisioner -> FabPartitionProvisioner",
        "Scoped IEventRepository -> EventRepository",
        "Scoped IWebhookIntegrationRepository -> WebhookIntegrationRepository",
        "Scoped IDeadLetterRepository -> DeadLetterRepository",
        "Scoped IRegisteredEventTypeRepository -> RegisteredEventTypeRepository",
        "Scoped IEventQuerySource -> EventQuerySource",
        "Scoped IDeadLetterQuerySource -> DeadLetterQuerySource",
        "Scoped IWebhookIntegrationQuerySource -> WebhookIntegrationQuerySource",
        "Scoped IRegisteredEventTypeQuerySource -> RegisteredEventTypeQuerySource",
        "Scoped ISourceModeRepository -> SourceModeRepository",
        "Scoped ISourceModeQuerySource -> SourceModeQuerySource",
        "Scoped IEventTypeAdmissionSource -> EventTypeAdmissionSource",
        "Scoped EventTypeAdmission -> EventTypeAdmission",
        "Scoped IDomainEventDispatcher -> DomainEventDispatcher",
        "Singleton IClock -> SystemClock",
        "Scoped IIdempotencyStore -> IdempotencyStore<EventIngestionDbContext>",
        "Singleton IHostedService -> IdempotencyReservationSweepHostedService<EventIngestionDbContext>",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Singleton IFabStorageReadiness -> CatalogFabStorageReadiness",
        "Scoped IDomainEventHandler<EventIngestedDomainEvent> -> EventIngestedDomainEventHandler",
        "Scoped IDomainEventHandler<WebhookIntegrationRevokedDomainEvent> -> WebhookIntegrationRevokedDomainEventHandler",
        "Scoped IDomainEventHandler<WebhookIntegrationRegisteredDomainEvent> -> WebhookIntegrationRegisteredDomainEventHandler",
        "Scoped IngestEventCommandHandler -> IngestEventCommandHandler",
        "Scoped IngestEventBatchCommandHandler -> IngestEventBatchCommandHandler",
        "Scoped ICommandHandler<IngestEventCommand, Result<EventIdentifier, IngestEventError>> -> IngestEventCommandHandler",
        "Scoped RegisterWebhookIntegrationCommandHandler -> RegisterWebhookIntegrationCommandHandler",
        "Scoped ICommandHandler<RegisterWebhookIntegrationCommand, Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>> -> RegisterWebhookIntegrationCommandHandler",
        "Scoped RevokeWebhookIntegrationCommandHandler -> RevokeWebhookIntegrationCommandHandler",
        "Scoped ICommandHandler<RevokeWebhookIntegrationCommand, Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>> -> RevokeWebhookIntegrationCommandHandler",
        "Scoped RegisterEventTypeCommandHandler -> RegisterEventTypeCommandHandler",
        "Scoped ICommandHandler<RegisterEventTypeCommand, Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>> -> RegisterEventTypeCommandHandler",
        "Scoped RetireEventTypeCommandHandler -> RetireEventTypeCommandHandler",
        "Scoped ICommandHandler<RetireEventTypeCommand, Result<RegisteredEventTypeIdentifier, RetireEventTypeError>> -> RetireEventTypeCommandHandler",
        "Scoped DeclareSourceModeCommandHandler -> DeclareSourceModeCommandHandler",
        "Scoped ICommandHandler<DeclareSourceModeCommand, Result<SourceModeIdentifier, DeclareSourceModeError>> -> DeclareSourceModeCommandHandler",
        "Scoped ChangeSourceModeCommandHandler -> ChangeSourceModeCommandHandler",
        "Scoped ICommandHandler<ChangeSourceModeCommand, Result<SourceModeIdentifier, ChangeSourceModeError>> -> ChangeSourceModeCommandHandler",
        "Scoped UndeclareSourceModeCommandHandler -> UndeclareSourceModeCommandHandler",
        "Scoped ICommandHandler<UndeclareSourceModeCommand, Result<SourceModeIdentifier, UndeclareSourceModeError>> -> UndeclareSourceModeCommandHandler",
        "Scoped GetEventQueryHandler -> GetEventQueryHandler",
        "Scoped ListEventsQueryHandler -> ListEventsQueryHandler",
        "Scoped ListDeadLettersQueryHandler -> ListDeadLettersQueryHandler",
        "Scoped ListWebhookIntegrationsQueryHandler -> ListWebhookIntegrationsQueryHandler",
        "Scoped ListEventTypesQueryHandler -> ListEventTypesQueryHandler",
        "Scoped ListSourceModesQueryHandler -> ListSourceModesQueryHandler",
        "Singleton BoundedIngestChannel -> factory",
        "Singleton IDisposable -> factory",
        "Singleton IIngestChannel -> factory",
        "Singleton IngestWriteLimiter -> factory",
        "Singleton IOptionsChangeTokenSource<IngestRetryOptions> -> instance of ConfigurationChangeTokenSource<IngestRetryOptions>",
        "Singleton IConfigureOptions<IngestRetryOptions> -> instance of NamedConfigureFromConfigurationOptions<IngestRetryOptions>",
        "Singleton IOptionsChangeTokenSource<IngestWriteOptions> -> instance of ConfigurationChangeTokenSource<IngestWriteOptions>",
        "Singleton IConfigureOptions<IngestWriteOptions> -> instance of NamedConfigureFromConfigurationOptions<IngestWriteOptions>",
        "Singleton IOptionsChangeTokenSource<MosquittoOptions> -> instance of ConfigurationChangeTokenSource<MosquittoOptions>",
        "Singleton IConfigureOptions<MosquittoOptions> -> instance of NamedConfigureFromConfigurationOptions<MosquittoOptions>",
        "Singleton IPostConfigureOptions<MosquittoOptions> -> instance of PostConfigureOptions<MosquittoOptions>",
        "Singleton MqttTokenProvider -> MqttTokenProvider",
        "Singleton MosquittoConnectionFactory -> MosquittoConnectionFactory",
        "Singleton IHostedService -> MqttSubscriberHostedService",
        "Singleton IHostedService -> PersistenceLoopHostedService",
        "Scoped IEventBus -> OutboxEventBus<EventIngestionDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<EventIngestionDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "event-ingestion",
            builder => captured = [.. builder.Services]);
        return [.. captured];
    }

    private static bool IsContextAssembly(Type? type) =>
        type?.Assembly.GetName().Name?.StartsWith(ContextAssemblyPrefix, StringComparison.Ordinal) == true;

    private static bool MentionsContext(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (IsContextAssembly(type))
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

        return MentionsContext(descriptor.ServiceType)
            || MentionsContext(descriptor.ImplementationType)
            || IsContextAssembly(descriptor.ImplementationFactory?.Method.DeclaringType);
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
    public void The_event_ingestion_registrations_are_these_in_this_order()
    {
        var registrations = CaptureComposedRegistrations()
            .Where(IsPinned)
            .Select(Render)
            .ToArray();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_mqtt_subscriber_is_registered_before_the_persistence_loop()
    {
        var hostedServices = CaptureComposedRegistrations()
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        var subscriber = hostedServices.IndexOf(typeof(MqttSubscriberHostedService));
        var loop = hostedServices.IndexOf(typeof(PersistenceLoopHostedService));

        subscriber.ShouldBeGreaterThan(-1);
        loop.ShouldBeGreaterThan(subscriber);
    }

    [Fact]
    public void The_mqtt_token_client_opts_back_in_to_retrying_every_method()
    {
        using var provider = PostgresComposition.Compose("event-ingestion");

        RetryEveryMethodMarkerChain.Carries(provider, MqttTokenProvider.HttpClientName).ShouldBeTrue();
    }
}
