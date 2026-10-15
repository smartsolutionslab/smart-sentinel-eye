using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Revocation;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Characterisation (spec 369, ADR-0144), captured green on unmodified
/// <c>AddBearerAuthentication</c> before it is split. The registration order is
/// observable and nothing else pins it: the container resolves last-wins, every
/// <c>Configure</c> of the <c>Bearer</c> options runs in registration order on
/// one instance, and the first exception handler that answers wins.
///
/// <para>
/// <b>What is pinned.</b> The slice of <c>builder.Services</c> the call added,
/// filtered to the families that carry the contract: any service or
/// implementation type from a <c>SmartSentinelEye.*</c> assembly (recursively
/// through generic arguments, keyed descriptors included),
/// <see cref="TimeProvider"/>, <see cref="IHostedService"/>,
/// <see cref="IExceptionHandler"/>, and the options configure / post-configure /
/// validate descriptors for the option types this composition touches. No total
/// count: framework growth says nothing about this composition. The scope
/// policies (one <c>AuthorizationOptions</c> configure per scope) are left out
/// for the same reason: their number moves whenever a scope is added.
/// </para>
///
/// <para>
/// Rendered as <c>{Lifetime} {Service} -> {Implementation}</c>, the shape of
/// spec 347/348's registration snapshots, made keyed-safe because a keyed
/// descriptor throws on <c>ImplementationType</c>.
/// </para>
/// </summary>
public class BearerAuthenticationRegistrationCharacterisationTests
{
    private const string OwnAssemblyPrefix = "SmartSentinelEye.";

    private static readonly Type[] PinnedServiceTypes =
    [
        typeof(TimeProvider),
        typeof(IHostedService),
        typeof(IExceptionHandler),
    ];

    private static readonly Type[] OptionsShapes =
    [
        typeof(IConfigureOptions<>),
        typeof(IPostConfigureOptions<>),
        typeof(IValidateOptions<>),
    ];

    private static readonly Type[] PinnedOptionTypes =
    [
        typeof(JwtBearerOptions),
        typeof(RevocationListOptions),
        typeof(AuthenticationOptions),
        typeof(HttpClientFactoryOptions),
        typeof(HttpStandardResilienceOptions),
        typeof(HealthCheckServiceOptions),
    ];

    private static readonly string[] ExpectedRegistrations =
    [
        "Singleton IHostedService -> DataProtectionHostedService",
        "Singleton TimeProvider -> instance of SystemTimeProvider",
        "Singleton IConfigureOptions<AuthenticationOptions> -> instance of ConfigureNamedOptions<AuthenticationOptions>",
        "Singleton IConfigureOptions<JwtBearerOptions> -> JwtBearerConfigureOptions",
        "Singleton IPostConfigureOptions<JwtBearerOptions> -> JwtBearerPostConfigureOptions",
        "Singleton IConfigureOptions<AuthenticationOptions> -> instance of ConfigureNamedOptions<AuthenticationOptions>",
        "Singleton IConfigureOptions<JwtBearerOptions> -> instance of ConfigureNamedOptions<JwtBearerOptions>",
        "Singleton IValidateOptions<JwtBearerOptions> -> instance of ValidateOptions<JwtBearerOptions>",
        "Singleton IPostConfigureOptions<JwtBearerOptions> -> PostConfigureAuthenticationSchemeOptions<JwtBearerOptions>",
        "Transient IConfigureOptions<JwtBearerOptions> -> factory",
        "Singleton IValidateOptions<JwtBearerOptions> -> instance of ValidateOptions<JwtBearerOptions>",
        "Singleton RevokedClientRegistry -> RevokedClientRegistry",
        "Singleton IRevokedClientRegistry -> factory",
        "Singleton IConfigureOptions<RevocationListOptions> -> instance of ConfigureNamedOptions<RevocationListOptions>",
        "Transient IValidateOptions<RevocationListOptions> -> factory",
        "Singleton IConfigureOptions<HttpStandardResilienceOptions> -> instance of ConfigureNamedOptions<HttpStandardResilienceOptions>",
        "Singleton RevocationListTokenProvider -> RevocationListTokenProvider",
        "Transient RevocationListAuthorizationHandler -> RevocationListAuthorizationHandler",
        "Singleton IConfigureOptions<HttpClientFactoryOptions> -> instance of ConfigureNamedOptions<HttpClientFactoryOptions>",
        "Singleton IConfigureOptions<HttpClientFactoryOptions> -> instance of ConfigureNamedOptions<HttpClientFactoryOptions>",
        "Singleton IRevokedClientSource -> HttpRevokedClientSource",
        "Singleton RevokedClientRefresher -> RevokedClientRefresher",
        "Singleton IHostedService -> factory",
        "Singleton IHostedService -> HealthCheckPublisherHostedService",
        "Singleton IConfigureOptions<HealthCheckServiceOptions> -> instance of ConfigureNamedOptions<HealthCheckServiceOptions>",
        "Singleton IFabAuthorizationGuard -> DefaultFabAuthorizationGuard",
        "Singleton IExceptionHandler -> BadHttpRequestExceptionHandler",
        "Singleton IExceptionHandler -> FabAuthorizationExceptionHandler",
        "Singleton IExceptionHandler -> UnattributableOperatorExceptionHandler",
        "Singleton IExceptionHandler -> ConcurrencyConflictExceptionHandler",
        "Singleton IExceptionHandler -> UniqueConstraintExceptionHandler",
    ];

    private static readonly string[] ExpectedExceptionHandlers =
    [
        "BadHttpRequestExceptionHandler",
        "FabAuthorizationExceptionHandler",
        "UnattributableOperatorExceptionHandler",
        "ConcurrencyConflictExceptionHandler",
        "UniqueConstraintExceptionHandler",
    ];

    [Fact]
    public void The_registrations_the_call_adds_are_these_in_this_order()
    {
        var registrations = RenderedSlice();

        registrations.ShouldBe(ExpectedRegistrations);
    }

    [Fact]
    public void The_rendered_slice_is_stable_across_two_composes()
    {
        RenderedSlice().ShouldBe(RenderedSlice());
    }

    [Fact]
    public void The_exception_handlers_are_registered_in_precedence_order()
    {
        var handlers = AddedSlice()
            .Where(descriptor => descriptor.ServiceType == typeof(IExceptionHandler))
            .Select(descriptor => descriptor.ImplementationType!.Name);

        handlers.ShouldBe(ExpectedExceptionHandlers);
    }

    private static string[] RenderedSlice() => [.. AddedSlice().Where(IsPinned).Select(Render)];

    private static List<ServiceDescriptor> AddedSlice()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://kc.invalid";
        var before = builder.Services.Count;

        builder.AddBearerAuthentication();

        return [.. builder.Services.Skip(before)];
    }

    private static bool IsPinned(ServiceDescriptor descriptor)
    {
        var serviceType = descriptor.ServiceType;

        if (PinnedServiceTypes.Contains(serviceType))
        {
            return true;
        }

        if (serviceType.IsGenericType
            && OptionsShapes.Contains(serviceType.GetGenericTypeDefinition())
            && PinnedOptionTypes.Contains(serviceType.GetGenericArguments()[0]))
        {
            return true;
        }

        return MentionsOwnAssembly(serviceType) || MentionsOwnAssembly(ImplementationTypeOf(descriptor));
    }

    private static bool MentionsOwnAssembly(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Assembly.GetName().Name?.StartsWith(OwnAssemblyPrefix, StringComparison.Ordinal) == true)
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(MentionsOwnAssembly);
    }

    private static Type? ImplementationTypeOf(ServiceDescriptor descriptor) =>
        descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;

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
        if (ImplementationTypeOf(descriptor) is { } implementationType)
        {
            return Name(implementationType);
        }

        var instance = descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance;
        return instance is not null ? $"instance of {Name(instance.GetType())}" : "factory";
    }

    private static string Render(ServiceDescriptor descriptor) =>
        $"{descriptor.Lifetime} {Name(descriptor.ServiceType)} -> {Implementation(descriptor)}";
}
