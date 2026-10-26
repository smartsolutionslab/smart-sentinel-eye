using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 350, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AddOverlayDesignerInfrastructure</c> is split into
/// private registration groups. The refactor must not change a lifetime, an
/// implementation or the order registrations are made in: the container's
/// last-wins resolution makes order observable, and nothing else asserts it.
///
/// <para>
/// Counterfactual this file was seen to catch: swapping the
/// <c>IOverlayRepository</c> and <c>IOverlayQuerySource</c> registrations (or
/// changing one lifetime, or dropping one registration) fails
/// <see cref="The_overlay_designer_registrations_are_these_in_this_order"/>.
/// </para>
///
/// <para>
/// <b>What is pinned.</b> The registrations this context makes, in order: every
/// descriptor whose service type or implementation type mentions a
/// <c>SmartSentinelEye.OverlayDesigner.*</c> type (recursively through generic
/// arguments), plus the shared services the module registers that live in
/// foreign assemblies (<see cref="IClock"/>, <see cref="IDomainEventDispatcher"/>,
/// <see cref="TimeProvider"/>, <see cref="IIdempotencyStore"/>). EF Core's,
/// Wolverine's and JasperFx's own plumbing is left out, as is any total count:
/// both move with package bumps and say nothing about this module.
/// </para>
/// </summary>
public class OverlayDesignerRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.OverlayDesigner.";

    private static readonly Type[] SharedServicesTheModuleRegisters =
    [
        typeof(IClock),
        typeof(IDomainEventDispatcher),
        typeof(TimeProvider),
        typeof(IIdempotencyStore),
    ];

    private static readonly string[] FrameworkNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "JasperFx",
        "Wolverine",
    ];

    private static readonly string[] ExpectedRegistrations =
    [
        "Scoped OverlayDesignerDbContext -> factory",
        "Singleton IMigrator -> OverlayDesignerMigrator",
        "Singleton IMigrator -> OverlayColourCapMigrator",
        "Scoped IOverlayRepository -> OverlayRepository",
        "Scoped IOverlayQuerySource -> OverlayQuerySource",
        "Scoped IDomainEventHandler<OverlayRevisionPublishedDomainEvent> -> OverlayRevisionPublishedDomainEventHandler",
        "Scoped IDomainEventHandler<OverlayRevisionArchivedDomainEvent> -> OverlayRevisionArchivedDomainEventHandler",
        "Scoped IDomainEventDispatcher -> DomainEventDispatcher",
        "Singleton IClock -> SystemClock",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Scoped IIdempotencyStore -> IdempotencyStore<OverlayDesignerDbContext>",
        "Singleton IHostedService -> IdempotencyReservationSweepHostedService<OverlayDesignerDbContext>",
        "Scoped ICommandHandler<CreateOverlayDraftCommand, Result<OverlayIdentifier, CreateOverlayDraftError>> -> CreateOverlayDraftCommandHandler",
        "Scoped ICommandHandler<PublishRevisionCommand, Result<OverlayRevisionNumber, PublishRevisionError>> -> PublishRevisionCommandHandler",
        "Scoped ICommandHandler<ArchiveRevisionCommand, Result<OverlayRevisionNumber, ArchiveRevisionError>> -> ArchiveRevisionCommandHandler",
        "Scoped ICommandHandler<BranchDraftRevisionCommand, Result<OverlayRevisionNumber, BranchDraftRevisionError>> -> BranchDraftRevisionCommandHandler",
        "Scoped ICommandHandler<EditDraftRevisionCommand, Result<OverlayRevisionNumber, EditDraftRevisionError>> -> EditDraftRevisionCommandHandler",
        "Scoped ICommandHandler<RevertRevisionCommand, Result<OverlayRevisionNumber, RevertRevisionError>> -> RevertRevisionCommandHandler",
        "Scoped IEventBus -> OutboxEventBus<OverlayDesignerDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<OverlayDesignerDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "overlay-designer",
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
    public void The_overlay_designer_registrations_are_these_in_this_order()
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
            .Single(descriptor => descriptor.ServiceType == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<OverlayDesignerDbContext>))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(OverlayDesignerDbContext))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
