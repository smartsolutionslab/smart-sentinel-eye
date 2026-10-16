using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Telemetry;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 351, ADR-0144), captured green on unmodified
/// <c>develop</c> (<c>8ded100e</c>) before
/// <c>AddStreamDistributionInfrastructure</c> and <c>AddMediaMtxGateway</c> are
/// split into private registration groups. The refactor must not change a
/// lifetime, an implementation or the order registrations are made in: the
/// container's last-wins resolution and the hosted services' start order make
/// order observable, and nothing else asserts it.
///
/// <para>
/// <b>What is pinned.</b> The registrations this context makes, in order: every
/// descriptor whose service type, implementation type or implementation factory
/// is declared in a <c>SmartSentinelEye.StreamDistribution.*</c> assembly
/// (recursively through generic arguments), plus the shared services the module
/// registers that live in foreign assemblies (<see cref="IClock"/>,
/// <see cref="TimeProvider"/>). EF Core's, Wolverine's and JasperFx's own
/// plumbing is left out, as is any total count: both move with package bumps and
/// say nothing about this module. A factory renders as <c>factory</c>, never as
/// its compiler-generated name, which moves when the lambda moves to another
/// method. Two clients are pinned separately by walking their handler chain for
/// the <c>RetryEveryMethod()</c> marker: they are the ADR-0143 opt-ins that let a
/// retried MediaMTX <c>add/</c>/<c>patch/</c> and the camera-catalog token mint be
/// repeated.
/// </para>
///
/// <para>
/// Counterfactual this file was seen to catch: swapping two adjacent
/// command-handler registrations, or changing a lifetime, fails
/// <see cref="The_stream_distribution_registrations_are_these_in_this_order"/>;
/// deleting <c>.RetryEveryMethod()</c> from <c>AddMediaMtxGateway</c> fails
/// <see cref="The_mediamtx_gateway_client_opts_back_in_to_retrying_every_method"/>.
/// </para>
/// </summary>
public class StreamDistributionRegistrationCharacterisationTests
{
    private const string ContextAssemblyPrefix = "SmartSentinelEye.StreamDistribution.";

    private static readonly Type[] SharedServicesTheModuleRegisters =
    [
        typeof(IClock),
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
        "Scoped StreamDistributionDbContext -> factory",
        "Singleton IMigrator -> StreamDistributionMigrator",
        "Singleton IConfigureOptions<MediaMtxOptions> -> instance of ConfigureNamedOptions<MediaMtxOptions>",
        "Singleton IConfigureOptions<MediaMtxApiOptions> -> instance of ConfigureNamedOptions<MediaMtxApiOptions>",
        "Singleton IValidateOptions<MediaMtxApiOptions> -> instance of ValidateOptions<MediaMtxApiOptions>",
        "Singleton IConfigureOptions<WhepAuthOptions> -> instance of ConfigureNamedOptions<WhepAuthOptions>",
        "Singleton IValidateOptions<WhepAuthOptions> -> instance of ValidateOptions<WhepAuthOptions>",
        "Singleton IConfigureOptions<StreamFabAttributionOptions> -> instance of ConfigureNamedOptions<StreamFabAttributionOptions>",
        "Singleton CameraCatalogTokenProvider -> CameraCatalogTokenProvider",
        "Transient CameraCatalogAuthorizationHandler -> CameraCatalogAuthorizationHandler",
        "Transient ICameraFabLookup -> factory",
        "Singleton IConfigureOptions<MediaMtxSourceRepairOptions> -> instance of ConfigureNamedOptions<MediaMtxSourceRepairOptions>",
        "Singleton IValidateOptions<MediaMtxSourceRepairOptions> -> instance of ValidateOptions<MediaMtxSourceRepairOptions>",
        "Scoped IStreamRepository -> StreamRepository",
        "Scoped IStreamQuerySource -> StreamQuerySource",
        "Scoped IDomainEventHandler<StreamProvisionedDomainEvent> -> StreamProvisionedDomainEventHandler",
        "Scoped IDomainEventHandler<StreamHealthChangedDomainEvent> -> StreamHealthChangedDomainEventHandler",
        "Singleton IClock -> SystemClock",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Singleton IStreamWhepUrlBuilder -> MediaMtxWhepUrlBuilder",
        "Singleton IWhepAuthValidator -> WhepAuthValidator",
        "Singleton IMediaMtxApiAuthenticator -> MediaMtxApiAuthenticator",
        "Scoped ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>> -> ProvisionStreamCommandHandler",
        "Scoped ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>> -> ReportStreamHealthCommandHandler",
        "Scoped ICommandHandler<AuthorizeWhepCommand, Result<MediaMtxPath, AuthorizeWhepError>> -> AuthorizeWhepCommandHandler",
        "Scoped ICommandHandler<AuthorizeMediaMtxApiCommand, Result<Boolean, AuthorizeMediaMtxApiError>> -> AuthorizeMediaMtxApiCommandHandler",
        "Scoped ICommandHandler<RetireStreamCommand, Result<Option<StreamIdentifier>, RetireStreamError>> -> RetireStreamCommandHandler",
        "Scoped ICommandHandler<RepointStreamCommand, Result<Option<StreamIdentifier>, RepointStreamError>> -> RepointStreamCommandHandler",
        "Scoped GetStreamQueryHandler -> GetStreamQueryHandler",
        "Scoped ListStreamsQueryHandler -> ListStreamsQueryHandler",
        "Scoped CameraRegisteredIntegrationEventHandler -> CameraRegisteredIntegrationEventHandler",
        "Scoped CameraRetiredIntegrationEventHandler -> CameraRetiredIntegrationEventHandler",
        "Scoped CameraAddressChangedIntegrationEventHandler -> CameraAddressChangedIntegrationEventHandler",
        "Scoped StreamHealthChangedDomainEventHandler -> StreamHealthChangedDomainEventHandler",
        "Scoped StreamProvisionedDomainEventHandler -> StreamProvisionedDomainEventHandler",
        "Transient IRtspGateway -> factory",
        "Singleton IHostedService -> StreamHealthWatcher",
        "Singleton IHostedService -> MediaMtxReconciler",
        "Singleton IHostedService -> MediaMtxSourceRepairSweep",
        "Singleton IHostedService -> StreamFabAttributionService",
        "Scoped IEventBus -> OutboxEventBus<StreamDistributionDbContext>",
        "Scoped ITransactionalCommit -> OutboxTransactionalCommit<StreamDistributionDbContext>",
    ];

    private static ServiceDescriptor[] CaptureComposedRegistrations()
    {
        List<ServiceDescriptor> captured = [];
        using var provider = PostgresComposition.Compose(
            "stream-distribution",
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
    public void The_stream_distribution_registrations_are_these_in_this_order()
    {
        var registrations = CaptureComposedRegistrations()
            .Where(IsPinned)
            .Select(Render)
            .ToArray();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_mediamtx_gateway_client_opts_back_in_to_retrying_every_method()
    {
        using var provider = PostgresComposition.Compose("stream-distribution");

        RetryEveryMethodMarkerChain.Carries(provider, "IRtspGateway").ShouldBeTrue();
    }

    [Fact]
    public void The_camera_catalog_token_client_opts_back_in_to_retrying_every_method()
    {
        using var provider = PostgresComposition.Compose("stream-distribution");

        RetryEveryMethodMarkerChain.Carries(provider, "stream-distribution-attribution-token").ShouldBeTrue();
    }

    /// <summary>
    /// The negative half. The two facts above pass just as well for a marker added to
    /// every client through <c>ConfigureHttpClientDefaults</c> — which would retry every
    /// POST in every host. The camera-catalog lookup did not opt in, so its chain must not
    /// carry the marker. Its own authorization handler is asserted first, so a client name
    /// that resolved to an empty default chain cannot make this pass.
    /// </summary>
    [Fact]
    public void The_camera_fab_lookup_client_does_not_opt_in_to_retrying_every_method()
    {
        using var provider = PostgresComposition.Compose("stream-distribution");

        var chain = RetryEveryMethodMarkerChain.Chain(provider, "ICameraFabLookup");

        chain.ShouldContain("CameraCatalogAuthorizationHandler");
        chain.ShouldNotContain("RetryEveryMethodMarker");
    }

    [Fact]
    public void The_probe_log_demotion_is_one_telemetry_configuration_made_after_the_gateway_client_and_before_the_hosted_services()
    {
        var descriptors = CaptureComposedRegistrations();

        var telemetry = descriptors
            .Select((descriptor, index) => (descriptor, index))
            .Where(entry => entry.descriptor.ServiceType == typeof(IConfigureOptions<TelemetryOptions>))
            .Select(entry => entry.index)
            .ToArray();
        var gateway = Array.FindIndex(
            descriptors,
            descriptor => descriptor.ServiceType == typeof(IRtspGateway));
        var firstHostedService = Array.FindIndex(
            descriptors,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType?.Name == "StreamHealthWatcher");

        telemetry.Length.ShouldBe(1);
        gateway.ShouldBeGreaterThan(-1);
        telemetry[0].ShouldBeGreaterThan(gateway);
        telemetry[0].ShouldBeLessThan(firstHostedService);
    }

    [Fact]
    public void The_mediamtx_gateway_client_targets_the_management_url_with_the_fixed_label_and_the_secret_as_basic_auth()
    {
        using var provider = PostgresComposition.Compose(
            "stream-distribution",
            builder => builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["MediaMtxApi:Secret"] = "fake-secret-1" }));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("IRtspGateway");

        client.BaseAddress.ShouldBe(new Uri("http://127.0.0.1:9997"));
        client.DefaultRequestHeaders.Authorization.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization.Scheme.ShouldBe("Basic");
        client.DefaultRequestHeaders.Authorization.Parameter.ShouldBe("c3RyZWFtLWRpc3RyaWJ1dGlvbjpmYWtlLXNlY3JldC0x");
    }
}
