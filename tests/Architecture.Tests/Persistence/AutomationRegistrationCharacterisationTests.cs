using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AddAutomationInfrastructure</c> is split into
/// private registration groups. The refactor must not change a lifetime, an
/// implementation or the order registrations are made in: the container's
/// last-wins resolution makes order observable, and nothing else asserts it.
///
/// <para>
/// <b>What is pinned.</b> The registrations this context makes, in order: every
/// descriptor whose service type or implementation type mentions a
/// <c>SmartSentinelEye.Automation.*</c> type (recursively through generic
/// arguments), plus the three shared services the module registers that live in
/// foreign assemblies (<see cref="IClock"/>, <see cref="IDomainEventDispatcher"/>,
/// <see cref="TimeProvider"/>). EF Core's, Wolverine's and JasperFx's own
/// plumbing is left out, as is any total count: both move with package bumps and
/// say nothing about this module.
/// </para>
/// </summary>
public class AutomationRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.Automation.";

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
        "Scoped AutomationDbContext -> factory",
        "Singleton IMigrator -> AutomationMigrator",
        "Scoped IRuleRepository -> RuleRepository",
        "Scoped IDomainEventDispatcher -> DomainEventDispatcher",
        "Singleton IClock -> SystemClock",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Scoped IIdempotencyStore -> IdempotencyStore<AutomationDbContext>",
        "Singleton IHostedService -> IdempotencyReservationSweepHostedService<AutomationDbContext>",
        "Singleton IRuleCache -> InMemoryRuleCache",
        "Scoped RuleEvaluator -> RuleEvaluator",
        "Singleton IHostedService -> RuleCacheSeederHostedService",
        "Scoped FabEventIngestedV1Handler -> FabEventIngestedV1Handler",
        "Scoped CreateRuleCommandHandler -> CreateRuleCommandHandler",
        "Scoped ICommandHandler<CreateRuleCommand, Result<RuleIdentifier, CreateRuleError>> -> CreateRuleCommandHandler",
        "Scoped PublishRuleCommandHandler -> PublishRuleCommandHandler",
        "Scoped ICommandHandler<PublishRuleCommand, Result<RuleIdentifier, PublishRuleError>> -> PublishRuleCommandHandler",
        "Scoped ArchiveRuleCommandHandler -> ArchiveRuleCommandHandler",
        "Scoped ICommandHandler<ArchiveRuleCommand, Result<RuleIdentifier, ArchiveRuleError>> -> ArchiveRuleCommandHandler",
        "Scoped IRuleQuerySource -> RuleQuerySource",
        "Scoped GetRuleQueryHandler -> GetRuleQueryHandler",
        "Scoped IQueryHandler<GetRuleQuery, Result<RuleDto, GetRuleError>> -> GetRuleQueryHandler",
        "Scoped ListRulesQueryHandler -> ListRulesQueryHandler",
        "Scoped IQueryHandler<ListRulesQuery, Result<IReadOnlyList<RuleDto>, ListRulesError>> -> ListRulesQueryHandler",
        "Scoped DryRunRuleQueryHandler -> DryRunRuleQueryHandler",
        "Scoped IQueryHandler<DryRunRuleQuery, Result<DryRunResultDto, DryRunRuleError>> -> DryRunRuleQueryHandler",
        "Scoped IEventBus -> OutboxEventBus<AutomationDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<AutomationDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "automation",
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
    public void The_automation_registrations_are_these_in_this_order()
    {
        var registrations = RenderPinned();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_db_context_factory_is_a_singleton_and_the_db_context_is_scoped()
    {
        var registrations = CaptureComposedRegistrations();

        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(IDbContextFactory<AutomationDbContext>))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
        registrations
            .Single(descriptor => descriptor.ServiceType == typeof(AutomationDbContext))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
