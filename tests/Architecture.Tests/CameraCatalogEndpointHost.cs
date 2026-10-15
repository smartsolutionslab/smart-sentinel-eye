using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.CameraCatalog.Api;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the CameraCatalog register and patch endpoints (spec 366, #2931),
/// shared by the register and patch characterisation tests. The command handlers are the real
/// ones; only what sits behind them is a Moq double, so the code the endpoint extracts and the
/// arguments it hands the handlers are both exercised. Nothing here is authentication
/// middleware: the principal is set on the context.
/// </summary>
internal sealed class CameraCatalogEndpointHost : IAsyncDisposable
{
    private readonly WebApplication app;

    public CameraCatalogEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyScope scope, CancellationToken _) =>
                IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc))));
        Cameras
            .Setup(cameras => cameras.GetWithinFabAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<CameraIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Camera>.None);
        Cameras
            .Setup(cameras => cameras.ExistsByNameAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<CameraName>(),
                It.IsAny<Option<CameraIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var builder = WebApplication.CreateBuilder([]);
        builder.Services
            .AddSingleton(Cameras.Object)
            .AddSingleton(FabGuard.Object)
            .AddSingleton(IdempotencyStore.Object)
            .AddSingleton(Clock.Object)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new RegisterCameraCommandHandler(
                Cameras.Object, Clock.Object, NullLogger<RegisterCameraCommandHandler>.Instance))
            .AddSingleton(new RetireCameraCommandHandler(
                Cameras.Object, Clock.Object, NullLogger<RetireCameraCommandHandler>.Instance))
            .AddSingleton(new ChangeCameraAddressCommandHandler(
                Cameras.Object, Clock.Object, NullLogger<ChangeCameraAddressCommandHandler>.Instance))
            .AddSingleton(new RenameCameraCommandHandler(
                Cameras.Object, Clock.Object, NullLogger<RenameCameraCommandHandler>.Instance));
        app = builder.Build();
        app.MapCameraCatalogEndpoints();
    }

    public Mock<ICameraRepository> Cameras { get; } = new();

    public Mock<IFabAuthorizationGuard> FabGuard { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IClock> Clock { get; } = new();

    public static ClaimsPrincipal Operator(Guid subject, params string[] fabs) =>
        InProcessEndpointDriver.Operator(subject, fabs);

    /// <summary>A principal that holds one fab but carries no subject claim at all.</summary>
    public static ClaimsPrincipal WithoutSubject(string fab) =>
        new(new ClaimsIdentity([new Claim("groups", "/fabs/" + fab)], "test"));

    public Task<InProcessResponse> SendAsync(
        string method,
        string pattern,
        ClaimsPrincipal user,
        InProcessRequest request) =>
        InProcessEndpointDriver.SendAsync(app, method, pattern, user, request);

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
