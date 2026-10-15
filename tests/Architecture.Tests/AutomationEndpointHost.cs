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
using SmartSentinelEye.Automation.Api;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the Automation rule endpoints (spec 367, #2932), shared by the
/// route-table and the create characterisations. The mapped <see cref="RequestDelegate"/> is
/// driven directly, so no Docker is needed.
///
/// <para>
/// The create handler is the real one; only the rule repository, the clock, the fab guard and the
/// idempotency store behind it are Moq doubles. The other rule handlers are not registered: the
/// route-table test reads metadata and never sends them a request. Nothing here is authentication
/// middleware: the principal is set on the context, and scope policies are pinned by the
/// route-table baseline instead.
/// </para>
/// </summary>
internal sealed class AutomationEndpointHost : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private static readonly System.Text.Json.JsonSerializerOptions Relaxed =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly WebApplication app;

    public AutomationEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(Now);
        Rules
            .Setup(rules => rules.GetByNameAsync(
                It.IsAny<FabIdentifier>(), It.IsAny<RuleName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Rule>.None);
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
            .AddSingleton(new CreateRuleCommandHandler(
                Rules.Object, Clock.Object, NullLogger<CreateRuleCommandHandler>.Instance));
        app = builder.Build();
        app.MapRulesEndpoints();
    }

    public Mock<IFabAuthorizationGuard> FabGuard { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IRuleRepository> Rules { get; } = new();

    public Mock<IClock> Clock { get; } = new();

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

    /// <summary>Makes the store answer every claim as an already completed request for <paramref name="resource"/>.</summary>
    public void StoreReplaysAs(Guid resource) =>
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.CompletedWith(resource));

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
        string? bodyJson = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? query = null)
    {
        var endpoint = Endpoints.Single(candidate =>
            candidate.RoutePattern.RawText == pattern
            && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));

        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = method;
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
