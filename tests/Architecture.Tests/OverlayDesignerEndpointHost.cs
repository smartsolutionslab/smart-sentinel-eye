using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.OverlayDesigner.Api;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the OverlayDesigner draft write endpoints (spec 366, #2931). The
/// command handlers are the real ones; only what sits behind them is a Moq double, so the code
/// the endpoint extracts and the arguments it hands the handlers are both exercised. OverlayDesigner
/// resolves no fab, so there is no fab guard here.
/// </summary>
internal sealed class OverlayDesignerEndpointHost : IAsyncDisposable
{
    private readonly WebApplication app;

    public OverlayDesignerEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyScope scope, CancellationToken _) =>
                IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc))));
        Overlays
            .Setup(overlays => overlays.GetByIdentifierAsync(It.IsAny<OverlayIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Overlay>.None);
        Overlays
            .Setup(overlays => overlays.GetByNameAsync(It.IsAny<OverlayName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Overlay>.None);

        var builder = WebApplication.CreateBuilder([]);
        builder.Services
            .AddSingleton(Overlays.Object)
            .AddSingleton(IdempotencyStore.Object)
            .AddSingleton(Clock.Object)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new CreateOverlayDraftCommandHandler(
                Overlays.Object, Clock.Object, NullLogger<CreateOverlayDraftCommandHandler>.Instance))
            .AddSingleton(new EditDraftRevisionCommandHandler(
                Overlays.Object, Clock.Object, NullLogger<EditDraftRevisionCommandHandler>.Instance));
        app = builder.Build();
        app.MapOverlayEndpoints();
    }

    public Mock<IOverlayRepository> Overlays { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IClock> Clock { get; } = new();

    public static ClaimsPrincipal Operator(Guid subject) => InProcessEndpointDriver.Operator(subject);

    public Task<InProcessResponse> SendAsync(
        string method,
        string pattern,
        ClaimsPrincipal user,
        InProcessRequest request) =>
        InProcessEndpointDriver.SendAsync(app, method, pattern, user, request);

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
