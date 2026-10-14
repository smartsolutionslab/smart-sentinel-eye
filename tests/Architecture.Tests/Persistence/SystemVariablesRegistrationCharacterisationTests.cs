using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 349, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AddSystemVariablesInfrastructure</c> is split into
/// private registration groups. The refactor must not change a lifetime, an
/// implementation or the order registrations are made in: the container's
/// last-wins resolution makes order observable, and nothing else asserts it.
///
/// <para>
/// <b>What is pinned.</b> The registrations this context makes, in order: every
/// descriptor whose service type or implementation type mentions a
/// <c>SmartSentinelEye.SystemVariables.*</c> type (recursively through generic
/// arguments), plus the three shared services the module registers that live in
/// foreign assemblies (<see cref="IClock"/>, <see cref="IDomainEventDispatcher"/>,
/// <see cref="TimeProvider"/>). EF Core's, Wolverine's and JasperFx's own
/// plumbing is left out, as is any total count: both move with package bumps and
/// say nothing about this module. The list is hand-written; the Automation
/// module's identical test is the model for the harness, not for the values.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: registering <c>IVariableSnapshotBuilder</c> as a
/// singleton, or moving <c>IOverlayTextVersions</c> above the dedup store, turns
/// <see cref="The_system_variables_registrations_are_these_in_this_order"/> red.
/// </para>
/// </summary>
public class SystemVariablesRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.SystemVariables.";

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
        "Scoped SystemVariablesDbContext -> factory",
        "Singleton IMigrator -> SystemVariablesMigrator",
        "Scoped IVariableRepository -> VariableRepository",
        "Scoped IVariableQuerySource -> VariableQuerySource",
        "Scoped IDomainEventDispatcher -> DomainEventDispatcher",
        "Singleton IClock -> SystemClock",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Scoped IIdempotencyStore -> IdempotencyStore<SystemVariablesDbContext>",
        "Singleton IHostedService -> IdempotencyReservationSweepHostedService<SystemVariablesDbContext>",
        "Singleton IReverseIndex -> InMemoryReverseIndex",
        "Singleton IResolver -> Resolver",
        "Scoped IVariableSnapshotBuilder -> VariableSnapshotBuilder",
        "Scoped IDomainEventHandler<VariableDefinedDomainEvent> -> VariableDefinedDomainEventHandler",
        "Scoped IDomainEventHandler<VariableValueChangedDomainEvent> -> VariableValueChangedDomainEventHandler",
        "Scoped IDomainEventHandler<VariableArchivedDomainEvent> -> VariableArchivedDomainEventHandler",
        "Scoped ICommandHandler<DefineVariableCommand, Result<VariableIdentifier, DefineVariableError>> -> DefineVariableCommandHandler",
        "Scoped SetVariableValueCommandHandler -> SetVariableValueCommandHandler",
        "Scoped ICommandHandler<SetVariableValueCommand, Result<VariableIdentifier, SetVariableValueError>> -> SetVariableValueCommandHandler",
        "Scoped ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>> -> ArchiveVariableCommandHandler",
        "Scoped IVariableValueRequestDedupStore -> VariableValueRequestDedupStore",
        "Scoped SystemVariableValueRequestedV1Handler -> SystemVariableValueRequestedV1Handler",
        "Scoped IOverlayTextVersions -> OverlayTextVersionStore",
        "Singleton IConfigureOptions<ReverseIndexSeederOptions> -> instance of ConfigureNamedOptions<ReverseIndexSeederOptions>",
        "Singleton OverlayDesignerTokenProvider -> OverlayDesignerTokenProvider",
        "Transient OverlayDesignerAuthorizationHandler -> OverlayDesignerAuthorizationHandler",
        "Singleton IHostedService -> ReverseIndexSeederHostedService",
        "Scoped IEventBus -> OutboxEventBus<SystemVariablesDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<SystemVariablesDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "system-variables",
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

    internal static string[] RenderPinned() =>
        [.. CaptureComposedRegistrations().Where(IsPinned).Select(Render)];

    [Fact]
    public void The_system_variables_registrations_are_these_in_this_order()
    {
        var registrations = RenderPinned();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_db_context_factory_is_a_singleton_and_the_db_context_is_scoped()
    {
        var registrations = CaptureComposedRegistrations();

        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(IDbContextFactory<SystemVariablesDbContext>))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(SystemVariablesDbContext))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
