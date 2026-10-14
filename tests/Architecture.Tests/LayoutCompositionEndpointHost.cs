using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.LayoutComposition.Api;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the LayoutComposition write endpoints (spec 363, #2929), shared by the
/// layout and wall characterisation tests. The mapped <see cref="RequestDelegate"/> is driven
/// directly, the way <c>KioskLatencyRecordingCharacterisationTests</c> drives the stream
/// endpoint, so no Docker is needed.
///
/// <para>
/// The command handlers are the real ones; only what sits behind them is a Moq double, so the
/// code the endpoint extracts and the arguments it hands the handlers are both exercised. Nothing
/// here is authentication middleware: the principal is set on the context, and scope policies
/// are pinned by the route-table baseline instead.
/// </para>
/// </summary>
internal sealed class LayoutCompositionEndpointHost : IAsyncDisposable
{
    private readonly WebApplication app;

    public LayoutCompositionEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyScope scope, CancellationToken _) =>
                IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc))));
        Layouts
            .Setup(layouts => layouts.GetByIdentifierAsync(
                It.IsAny<IReadOnlyList<FabIdentifier>>(),
                It.IsAny<LayoutIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Layout>.None);
        Layouts
            .Setup(layouts => layouts.GetByNameAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<LayoutName>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Layout>.None);
        CameraFabs
            .Setup(guard => guard.CamerasOutsideFabAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<IReadOnlyList<CameraIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Walls
            .Setup(walls => walls.FindAsync(
                It.IsAny<WallIdentifier>(),
                It.IsAny<IReadOnlyList<FabIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Wall>.None);
        Walls
            .Setup(walls => walls.FindByNameAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<WallName>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Wall>.None);
        Lookup
            .Setup(lookup => lookup.PublishedAmong(
                It.IsAny<IEnumerable<LayoutIdentifier>>(),
                It.IsAny<FabIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<LayoutIdentifier>());
        Lookup
            .Setup(lookup => lookup.FabsOf(
                It.IsAny<IEnumerable<LayoutIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<LayoutIdentifier, FabIdentifier>());

        var builder = WebApplication.CreateBuilder([]);
        builder.Services
            .AddSingleton(Layouts.Object)
            .AddSingleton(Walls.Object)
            .AddSingleton(CameraFabs.Object)
            .AddSingleton(Lookup.Object)
            .AddSingleton(FabGuard.Object)
            .AddSingleton(IdempotencyStore.Object)
            .AddSingleton(Clock.Object)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new CreateLayoutDraftCommandHandler(
                Layouts.Object, CameraFabs.Object, Clock.Object, NullLogger<CreateLayoutDraftCommandHandler>.Instance))
            .AddSingleton(new PublishRevisionCommandHandler(
                Layouts.Object, Clock.Object, NullLogger<PublishRevisionCommandHandler>.Instance))
            .AddSingleton(new ArchiveRevisionCommandHandler(
                Layouts.Object, Clock.Object, NullLogger<ArchiveRevisionCommandHandler>.Instance))
            .AddSingleton(new BranchDraftRevisionCommandHandler(
                Layouts.Object, Clock.Object, NullLogger<BranchDraftRevisionCommandHandler>.Instance))
            .AddSingleton(new EditDraftRevisionCommandHandler(
                Layouts.Object, CameraFabs.Object, Clock.Object, NullLogger<EditDraftRevisionCommandHandler>.Instance))
            .AddSingleton(new RevertRevisionCommandHandler(
                Layouts.Object, Clock.Object, NullLogger<RevertRevisionCommandHandler>.Instance))
            .AddSingleton(new CreateWallCommandHandler(
                Walls.Object, Lookup.Object, Clock.Object, NullLogger<CreateWallCommandHandler>.Instance))
            .AddSingleton(new EditWallScenesCommandHandler(
                Walls.Object, Lookup.Object, Clock.Object, NullLogger<EditWallScenesCommandHandler>.Instance))
            .AddSingleton(new SwitchWallSceneCommandHandler(
                Walls.Object, Lookup.Object, Clock.Object, NullLogger<SwitchWallSceneCommandHandler>.Instance));
        app = builder.Build();
        app.MapLayoutEndpoints();
        app.MapWallEndpoints();
    }

    public Mock<ILayoutRepository> Layouts { get; } = new();

    public Mock<IWallRepository> Walls { get; } = new();

    public Mock<ICameraFabGuard> CameraFabs { get; } = new();

    public Mock<ILayoutPublicationLookup> Lookup { get; } = new();

    public Mock<IFabAuthorizationGuard> FabGuard { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IClock> Clock { get; } = new();

    public IEnumerable<RouteEndpoint> Endpoints =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>();

    public static ClaimsPrincipal Operator(Guid subject, params string[] fabs) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", subject.ToString()),
                .. fabs.Select(fab => new Claim("groups", "/fabs/" + fab)),
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
