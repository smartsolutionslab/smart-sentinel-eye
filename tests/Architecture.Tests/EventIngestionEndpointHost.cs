using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.EventIngestion.Api;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the EventIngestion endpoints (spec 365, #2930), shared by the
/// source-mode, event-type, webhook and event characterisation tests. The mapped
/// <see cref="RequestDelegate"/> is driven directly, the way
/// <c>KioskLatencyRecordingCharacterisationTests</c> drives the stream endpoint, so no Docker is
/// needed.
///
/// <para>
/// The command and query handlers are the real ones, and so are the write limiter and the memory
/// cache; only what sits behind them is a Moq double. The code an endpoint extracts and the
/// arguments it hands the handlers are therefore both exercised. Nothing here is authentication
/// middleware: the principal is set on the context, and scope policies are pinned by the
/// route-table baseline instead. The JWT webhook mode asks <see cref="IAuthenticationService"/>,
/// which is a double.
/// </para>
/// </summary>
internal sealed class EventIngestionEndpointHost : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly WebApplication app;

    public EventIngestionEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(Now);
        Storage
            .Setup(storage => storage.IsReadyAsync(It.IsAny<FabIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyScope scope, CancellationToken _) =>
                IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, Now.UtcDateTime)));
        Integrations
            .Setup(integrations => integrations.GetByNameAsync(It.IsAny<WebhookIntegrationName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<WebhookIntegration>.None);
        Integrations
            .Setup(integrations => integrations.IsRevokedByKeycloakClientIdAsync(
                It.IsAny<KeycloakClientIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        Events
            .Setup(events => events.ExistsAsync(It.IsAny<FabIdentifier>(), It.IsAny<EventIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        AdmissionSource
            .Setup(source => source.DeclaredSourceModesAsync(
                It.IsAny<IReadOnlyCollection<FabIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(FabIdentifier Fab, Source Source), EventTypeMode>());
        SourceModes
            .Setup(sourceModes => sourceModes.GetAsync(It.IsAny<FabIdentifier>(), It.IsAny<Source>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<SourceMode>.None);
        EventTypes
            .Setup(eventTypes => eventTypes.GetRegisteredAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<Kind>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredEventType>.None);
        DeadLetters
            .Setup(deadLetters => deadLetters.PromoteHeldAsync(It.IsAny<FabIdentifier>(), It.IsAny<Kind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        Authentication
            .Setup(authentication => authentication.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());

        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(Logs);
        builder.Services
            .AddMemoryCache()
            .AddSingleton(FabGuard.Object)
            .AddSingleton(Storage.Object)
            .AddSingleton(IdempotencyStore.Object)
            .AddSingleton(Integrations.Object)
            .AddSingleton(Authentication.Object)
            .AddSingleton(Clock.Object)
            .AddSingleton(Limiter)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new DeclareSourceModeCommandHandler(
                SourceModes.Object, Clock.Object, NullLogger<DeclareSourceModeCommandHandler>.Instance))
            .AddSingleton(new ChangeSourceModeCommandHandler(
                SourceModes.Object, Clock.Object, NullLogger<ChangeSourceModeCommandHandler>.Instance))
            .AddSingleton(new RegisterEventTypeCommandHandler(
                EventTypes.Object, DeadLetters.Object, Clock.Object, NullLogger<RegisterEventTypeCommandHandler>.Instance))
            .AddSingleton(new RegisterWebhookIntegrationCommandHandler(
                Integrations.Object, Clock.Object, NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance))
            .AddSingleton(new ListEventsQueryHandler(EventQuery.Object))
            .AddScoped(_ => new IngestEventCommandHandler(
                Events.Object,
                DeadLetters.Object,
                Clock.Object,
                new EventTypeAdmission(AdmissionSource.Object),
                NullLogger<IngestEventCommandHandler>.Instance));
        app = builder.Build();
        app.MapEventsEndpoints();
        app.MapEventSourcesEndpoints();
        app.MapEventTypesEndpoints();
        app.MapWebhookIntegrationsEndpoints();
    }

    public Mock<IFabAuthorizationGuard> FabGuard { get; } = new();

    public Mock<IFabStorageReadiness> Storage { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IWebhookIntegrationRepository> Integrations { get; } = new();

    public Mock<IEventRepository> Events { get; } = new();

    public Mock<IDeadLetterRepository> DeadLetters { get; } = new();

    public Mock<ISourceModeRepository> SourceModes { get; } = new();

    public Mock<IRegisteredEventTypeRepository> EventTypes { get; } = new();

    public Mock<IEventTypeAdmissionSource> AdmissionSource { get; } = new();

    public Mock<IEventQuerySource> EventQuery { get; } = new();

    public Mock<IAuthenticationService> Authentication { get; } = new();

    public Mock<IClock> Clock { get; } = new();

    public IngestWriteLimiter Limiter { get; } = new(1);

    public CapturingLoggerProvider Logs { get; } = new();

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

    private static readonly System.Text.Json.JsonSerializerOptions Relaxed =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The one scope the idempotency store was asked to reserve.</summary>
    public IdempotencyScope BegunScope() =>
        IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();

    public static ClaimsPrincipal Operator(Guid subject, params string[] fabs) => OperatorFrom(subject, null, fabs);

    /// <summary>An operator whose token also names the Keycloak client it was minted for (<c>azp</c>).</summary>
    public static ClaimsPrincipal OperatorFrom(Guid subject, string? authorizedParty, params string[] fabs) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", subject.ToString()),
                .. fabs.Select(fab => new Claim("groups", "/fabs/" + fab)),
                .. authorizedParty is null ? Array.Empty<Claim>() : [new Claim("azp", authorizedParty)],
            ],
            "test"));

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

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        Limiter.Dispose();
    }

    internal sealed record HostResponse(int Status, string Body, string Location);

    internal sealed record LoggedMessage(LogLevel Level, string Text);

    /// <summary>Keeps every record the endpoints log, so the revoked-delivery log can be counted.</summary>
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LoggedMessage> messages = [];

        public IReadOnlyList<LoggedMessage> Messages
        {
            get
            {
                lock (messages)
                {
                    return [.. messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Add(LoggedMessage message)
        {
            lock (messages)
            {
                messages.Add(message);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Add(new LoggedMessage(logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// A <see cref="DefaultHttpContext"/> has no server in front of it to say a request can carry
    /// a body, and without that the binder reports the body as not provided.
    /// </summary>
    private sealed class BodyPresent : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
