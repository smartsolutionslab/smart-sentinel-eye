using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 345, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AddLayoutCompositionInfrastructure</c> is split into
/// private registration groups. The refactor must not change a lifetime, an
/// implementation or the order registrations are made in: the container's
/// last-wins resolution makes order observable, and nothing else asserts it.
///
/// <para>
/// <b>What is pinned.</b> The registrations this context makes, in order: every
/// descriptor whose service type or implementation type mentions a
/// <c>SmartSentinelEye.LayoutComposition.*</c> type (recursively through generic
/// arguments), plus the four shared services the module registers that live in
/// foreign assemblies (<see cref="IClock"/>, <see cref="IDomainEventDispatcher"/>,
/// <see cref="TimeProvider"/>, <see cref="IHttpContextAccessor"/>). EF Core's,
/// Wolverine's and JasperFx's own plumbing is left out, as is any total count:
/// both move with package bumps and say nothing about this module.
/// </para>
/// </summary>
public class LayoutCompositionRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.LayoutComposition.";

    private static readonly Type[] SharedServicesTheModuleRegisters =
    [
        typeof(IClock),
        typeof(IDomainEventDispatcher),
        typeof(TimeProvider),
        typeof(IHttpContextAccessor),
    ];

    private static readonly string[] FrameworkNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "JasperFx",
        "Wolverine",
    ];

    private static readonly string[] ExpectedRegistrations =
    [
        "Scoped LayoutCompositionDbContext -> factory",
        "Singleton IMigrator -> LayoutCompositionMigrator",
        "Scoped ILayoutRepository -> LayoutRepository",
        "Scoped ILayoutQuerySource -> LayoutQuerySource",
        "Scoped IWallRepository -> WallRepository",
        "Scoped ILayoutPublicationLookup -> LayoutPublicationLookup",
        "Scoped IWallSwitchRequestDedupStore -> WallSwitchRequestDedupStore",
        "Scoped IDomainEventHandler<LayoutRevisionPublishedDomainEvent> -> LayoutRevisionPublishedDomainEventHandler",
        "Scoped IDomainEventHandler<LayoutRevisionArchivedDomainEvent> -> LayoutRevisionArchivedDomainEventHandler",
        "Scoped IDomainEventHandler<WallConfiguredDomainEvent> -> WallConfiguredDomainEventHandler",
        "Scoped IDomainEventHandler<WallSceneSwitchedDomainEvent> -> WallSceneSwitchedDomainEventHandler",
        "Scoped IDomainEventDispatcher -> DomainEventDispatcher",
        "Singleton IClock -> SystemClock",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Scoped IIdempotencyStore -> IdempotencyStore<LayoutCompositionDbContext>",
        "Singleton IHostedService -> IdempotencyReservationSweepHostedService<LayoutCompositionDbContext>",
        "Singleton ILayoutLifecycleBroadcaster -> SignalRLayoutLifecycleBroadcaster",
        "Singleton IHttpContextAccessor -> HttpContextAccessor",
        "Transient CallerTokenForwardingHandler -> CallerTokenForwardingHandler",
        "Transient ICameraFabGuard -> factory",
        "Scoped ICommandHandler<CreateLayoutDraftCommand, Result<LayoutIdentifier, CreateLayoutDraftError>> -> CreateLayoutDraftCommandHandler",
        "Scoped ICommandHandler<PublishRevisionCommand, Result<LayoutRevisionNumber, PublishRevisionError>> -> PublishRevisionCommandHandler",
        "Scoped ICommandHandler<ArchiveRevisionCommand, Result<LayoutRevisionNumber, ArchiveRevisionError>> -> ArchiveRevisionCommandHandler",
        "Scoped ICommandHandler<BranchDraftRevisionCommand, Result<LayoutRevisionNumber, BranchDraftRevisionError>> -> BranchDraftRevisionCommandHandler",
        "Scoped ICommandHandler<EditDraftRevisionCommand, Result<LayoutRevisionNumber, EditDraftRevisionError>> -> EditDraftRevisionCommandHandler",
        "Scoped ICommandHandler<RevertRevisionCommand, Result<LayoutRevisionNumber, RevertRevisionError>> -> RevertRevisionCommandHandler",
        "Scoped ICommandHandler<CreateWallCommand, Result<WallIdentifier, CreateWallError>> -> CreateWallCommandHandler",
        "Scoped ICommandHandler<EditWallScenesCommand, Result<WallDto, EditWallScenesError>> -> EditWallScenesCommandHandler",
        "Scoped ICommandHandler<SwitchWallSceneCommand, Result<WallDto, SwitchWallSceneError>> -> SwitchWallSceneCommandHandler",
        "Scoped OverlayHighlightRequestedV1Handler -> OverlayHighlightRequestedV1Handler",
        "Scoped OverlayRevisionPublishedV3Handler -> OverlayRevisionPublishedV3Handler",
        "Scoped OverlayRevisionArchivedV1Handler -> OverlayRevisionArchivedV1Handler",
        "Scoped ResolvedOverlayTextChangedV3Handler -> ResolvedOverlayTextChangedV3Handler",
        "Scoped WallSceneSwitchRequestedV1Handler -> WallSceneSwitchRequestedV1Handler",
        "Scoped WallSceneChangedV1Handler -> WallSceneChangedV1Handler",
        "Scoped IEventBus -> OutboxEventBus<LayoutCompositionDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<LayoutCompositionDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "layout-composition",
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
    public void The_layout_composition_registrations_are_these_in_this_order()
    {
        var registrations = CaptureComposedRegistrations()
            .Where(IsPinned)
            .Select(Render)
            .ToArray();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_db_context_factory_is_a_singleton_and_the_db_context_is_scoped()
    {
        var registrations = CaptureComposedRegistrations();

        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<LayoutCompositionDbContext>))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(LayoutCompositionDbContext))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Signalr_is_added_so_the_hub_context_is_registered()
    {
        var registrations = CaptureComposedRegistrations();

        registrations.ShouldContain(
            descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IHubContext<>));
    }
}
