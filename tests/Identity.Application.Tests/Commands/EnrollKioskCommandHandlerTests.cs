using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

public class EnrollKioskCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static EnrollKioskCommand HappyCommand(string clientId = "kiosk-3") =>
        new(
            ClientId.From(clientId),
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()));

    [Fact]
    public async Task Happy_path_creates_a_Keycloak_client_and_returns_the_minted_secret()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientSecret.ShouldBe("secret-kiosk-3");
        result.Value.Fab.ShouldBe("munich");
        repo.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.Kiosk);
    }

    [Fact]
    public async Task Re_enrollment_with_an_active_kiosk_returns_KioskAlreadyEnrolled()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> first =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);
        first.IsSuccess.ShouldBeTrue();

        Result<KioskCredentialsDto, EnrollKioskError> second =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        second.IsSuccess.ShouldBeFalse();
        second.Error.ShouldBeOfType<EnrollKioskError.KioskAlreadyEnrolled>();
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "503 from Keycloak" };
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<EnrollKioskError.KeycloakUnavailable>();
        repo.Clients.ShouldBeEmpty();
    }
}
