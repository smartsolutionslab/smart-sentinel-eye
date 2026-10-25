using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.Identity.Api;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the Identity device, kiosk and webhook-rotation endpoints (spec 367,
/// #2932), shared by their route-table and behaviour characterisations. The mapped
/// <see cref="RequestDelegate"/> is driven directly, so no Docker is needed.
///
/// <para>
/// The register, enrol, rotate and replay handlers are the real ones; only what sits behind them
/// (the repository, the Keycloak admin port, the integration-status lookup, the event bus, the
/// commit, the clock, the inner disable handler, the fab guard and the idempotency store) is a
/// Moq double. The code an endpoint extracts and the arguments it hands the handlers are
/// therefore both exercised, and "the replay never reached Keycloak" is a fact about the
/// <see cref="Keycloak"/> double rather than a claim about the endpoint. Nothing here is
/// authentication middleware: the principal is set on the context, and scope policies are pinned
/// by the route-table baseline instead.
/// </para>
/// </summary>
internal sealed class IdentityEndpointHost : IAsyncDisposable
{
    public const string CreatedSecret = "secret-created-by-keycloak";

    public const string RotatedSecret = "secret-rotated-by-keycloak";

    public const string ReplayedSecret = "secret-read-back-from-keycloak";

    public static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private static readonly System.Text.Json.JsonSerializerOptions Relaxed =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly WebApplication app;

    public IdentityEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(Now);
        Clients
            .Setup(clients => clients.GetWithinFabAsync(
                It.IsAny<FabIdentifier>(), It.IsAny<ClientId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredClientAggregate>.None);
        Clients
            .Setup(clients => clients.GetByIdentifierAsync(
                It.IsAny<RegisteredClientIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredClientAggregate>.None);
        Keycloak
            .Setup(keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeycloakClientCredentials(CreatedSecret));
        Keycloak
            .Setup(keycloak => keycloak.RotateClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeycloakClientCredentials(RotatedSecret));
        Keycloak
            .Setup(keycloak => keycloak.ReadClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeycloakClientCredentials(ReplayedSecret));
        IntegrationStatus
            .Setup(status => status.GetStatusAsync(
                It.IsAny<FabIdentifier>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookIntegrationStatus.Active);
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyScope scope, CancellationToken _) =>
                IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, Now.UtcDateTime)));

        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Trace);
        builder.Services
            .AddSingleton(FabGuard.Object)
            .AddSingleton(IdempotencyStore.Object)
            .AddSingleton(Clock.Object)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new RegisterDeviceCommandHandler(
                Clients.Object, Keycloak.Object, Clock.Object, NullLogger<RegisterDeviceCommandHandler>.Instance))
            .AddSingleton(new EnrollKioskCommandHandler(
                Clients.Object, Keycloak.Object, Clock.Object, NullLogger<EnrollKioskCommandHandler>.Instance))
            .AddSingleton(new RotateWebhookClientCommandHandler(
                Clients.Object,
                Keycloak.Object,
                IntegrationStatus.Object,
                Events.Object,
                Commit.Object,
                Clock.Object,
                DisableWebhookClient.Object,
                TimeProvider.System,
                NullLogger<RotateWebhookClientCommandHandler>.Instance))
            .AddSingleton(new ReplayRegisteredClientQueryHandler(Clients.Object, Keycloak.Object))
            .AddSingleton(new ListDevicesQueryHandler(QuerySource))
            .AddSingleton(new ListKiosksQueryHandler(QuerySource))
            .AddSingleton(new ListWebhookClientsQueryHandler(QuerySource));
        app = builder.Build();
        app.MapDevicesEndpoints();
        app.MapKiosksEndpoints();
        app.MapWebhookRotationEndpoints();
    }

    public Mock<IFabAuthorizationGuard> FabGuard { get; } = new();

    public CountingRegisteredClientQuerySource QuerySource { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IRegisteredClientRepository> Clients { get; } = new();

    public Mock<IKeycloakAdminClient> Keycloak { get; } = new();

    public Mock<IWebhookIntegrationStatusLookup> IntegrationStatus { get; } = new();

    public Mock<IEventBus> Events { get; } = new();

    public Mock<ITransactionalCommit> Commit { get; } = new();

    public Mock<IClock> Clock { get; } = new();

    public Mock<ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>> DisableWebhookClient { get; } = new();

    public IEnumerable<RouteEndpoint> Endpoints =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>();

    /// <summary>The JSON a problem result is written as: type first when the framework supplies one, then title, status, detail.</summary>
    public static string Problem(int status, string title, string detail, string? type = null) =>
        System.Text.Json.JsonSerializer.Serialize(
            type is null
                ? new Dictionary<string, object> { ["title"] = title, ["status"] = status, ["detail"] = detail }
                : new Dictionary<string, object> { ["type"] = type, ["title"] = title, ["status"] = status, ["detail"] = detail },
            Relaxed);

    public static ClaimsPrincipal Operator(Guid subject, params string[] fabs) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", subject.ToString()),
                .. fabs.Select(fab => new Claim("groups", "/fabs/" + fab)),
            ],
            "test"));

    /// <summary>A registered client the repository will hand back for a replay, at the version the replay should report.</summary>
    public RegisteredClientAggregate Held(
        RegisteredClientIdentifier identifier,
        string clientId,
        ClientKind kind,
        string fab,
        int version)
    {
        var held = RegisteredClientAggregate.Register(
            ClientId.From(clientId), kind, FabIdentifier.From(fab), OperatorIdentifier.From(Guid.NewGuid()), Clock.Object);
        typeof(AggregateRoot<RegisteredClientIdentifier>)
            .GetProperty(nameof(AggregateRoot<RegisteredClientIdentifier>.Version))!
            .SetValue(held, new AggregateVersion(version));
        typeof(AggregateRoot<RegisteredClientIdentifier>)
            .GetProperty(nameof(AggregateRoot<RegisteredClientIdentifier>.Id))!
            .SetValue(held, identifier);
        Clients
            .Setup(clients => clients.GetByIdentifierAsync(identifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredClientAggregate>.Some(held));

        return held;
    }

    /// <summary>Puts a registered client of <paramref name="kind"/> in <paramref name="fab"/> where the list handlers will find it.</summary>
    public void Seed(string clientId, ClientKind kind, string fab) =>
        QuerySource.Add(RegisteredClientAggregate.Register(
            ClientId.From(clientId), kind, FabIdentifier.From(fab), OperatorIdentifier.From(Guid.NewGuid()), Clock.Object));

    /// <summary>The client identifiers a list answer carries, so a wrong fab set fails by name rather than by count.</summary>
    public static IReadOnlyList<string> ClientIds(HostResponse response) =>
        [.. System.Text.Json.JsonDocument.Parse(response.Body).RootElement.EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString()!)];

    /// <summary>
    /// Makes the store answer every claim as an already completed request for <paramref name="resource"/>,
    /// recording <paramref name="version"/> against it when given (only the webhook rotate route reads it).
    /// </summary>
    public void StoreReplaysAs(Guid resource, int? version = null) =>
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(version is { } recorded
                ? IdempotencyReservation.CompletedWith(
                    resource, Option<AggregateVersion>.Some(AggregateVersion.From(recorded)))
                : IdempotencyReservation.CompletedWith(resource));

    /// <summary>Makes the fab guard refuse every caller, the way the real guard does for a fab they do not hold.</summary>
    public void GuardRefuses(string fab) =>
        FabGuard
            .Setup(guard => guard.EnsureAccessAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FabAuthorizationException(fab));

    /// <summary>The one scope the idempotency store was asked to reserve.</summary>
    public IdempotencyScope BegunScope() =>
        IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();

    /// <summary>
    /// Sends a request to the endpoint mapped at <paramref name="pattern"/> for
    /// <paramref name="method"/>, with the route values the template would have bound.
    /// </summary>
    public async Task<HostResponse> SendAsync(
        string method,
        string pattern,
        ClaimsPrincipal user,
        IReadOnlyDictionary<string, string>? route = null,
        string? bodyJson = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? query = null)
    {
        var endpoint = Endpoints.Single(candidate =>
            candidate.RoutePattern.RawText == pattern
            && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));

        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = method;
        foreach (var (name, value) in route ?? new Dictionary<string, string>())
        {
            context.Request.RouteValues[name] = value;
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            context.Request.Headers[name] = value;
        }

        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        if (bodyJson is not null)
        {
            var payload = Encoding.UTF8.GetBytes(bodyJson);
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(payload);
            context.Request.ContentLength = payload.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyPresent());
        }

        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var location = context.Response.Headers.Location.ToString();

        return new HostResponse(context.Response.StatusCode, await reader.ReadToEndAsync(), location);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    internal sealed record HostResponse(int Status, string Body, string Location);

    /// <summary>
    /// A <see cref="DefaultHttpContext"/> has no server in front of it to say a request can carry
    /// a body, and without that the binder reports the body as not provided.
    /// </summary>
    private sealed class BodyPresent : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
