using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Api;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// An in-process host for the SystemVariables define endpoint (spec 366, #2931). The define
/// handler is the real one; only what sits behind it is a Moq double, so the code the endpoint
/// extracts and the arguments it hands the handler are both exercised. Nothing here is
/// authentication middleware: the principal is set on the context.
/// </summary>
internal sealed class SystemVariablesEndpointHost : IAsyncDisposable
{
    private readonly WebApplication app;

    public SystemVariablesEndpointHost()
    {
        Clock.Setup(clock => clock.UtcNow).Returns(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
        IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyScope scope, CancellationToken _) =>
                IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc))));
        Variables
            .Setup(variables => variables.GetByNameAsync(
                It.IsAny<FabIdentifier>(),
                It.IsAny<VariableName>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Variable>.None);

        var builder = WebApplication.CreateBuilder([]);
        builder.Services
            .AddSingleton(Variables.Object)
            .AddSingleton(FabGuard.Object)
            .AddSingleton(IdempotencyStore.Object)
            .AddSingleton(Clock.Object)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new DefineVariableCommandHandler(
                Variables.Object, Clock.Object, NullLogger<DefineVariableCommandHandler>.Instance));
        app = builder.Build();
        app.MapSystemVariableEndpoints();
    }

    public Mock<IVariableRepository> Variables { get; } = new();

    public Mock<IFabAuthorizationGuard> FabGuard { get; } = new();

    public Mock<IIdempotencyStore> IdempotencyStore { get; } = new();

    public Mock<IClock> Clock { get; } = new();

    public static ClaimsPrincipal Operator(Guid subject, params string[] fabs) =>
        InProcessEndpointDriver.Operator(subject, fabs);

    public Task<InProcessResponse> SendAsync(
        string method,
        string pattern,
        ClaimsPrincipal user,
        InProcessRequest request) =>
        InProcessEndpointDriver.SendAsync(app, method, pattern, user, request);

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
