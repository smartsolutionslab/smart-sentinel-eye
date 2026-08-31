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

    /// <summary>
    /// **An enrolment must not report success over an account that kept the
    /// privilege** (spec 052 US1).
    ///
    /// <para>
    /// The realm hands every account it creates a default privilege that mints
    /// credentials which never expire, and enrolment takes it back. If that
    /// removal fails and the enrolment still succeeds, the system has said
    /// "enrolled" about a kiosk holding exactly the credential this feature
    /// exists to withhold — and nothing downstream would ever mention it.
    /// </para>
    ///
    /// <para>
    /// Asserted on the reported outcome rather than on an exception type,
    /// because the handler's job is to turn that failure into a typed error.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Fails_the_enrolment_when_the_inherited_privilege_cannot_be_removed()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StripFailsFor.Add("kiosk-3");
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse(
            "an enrolment that succeeds here leaves a kiosk holding a credential that never expires");
        repo.Clients.ShouldBeEmpty("nothing should be recorded as enrolled when it was not");
    }

    /// <summary>
    /// The other half: a successful enrolment has actually taken the privilege
    /// back, rather than merely not failing.
    /// </summary>
    [Fact]
    public async Task Takes_the_inherited_privilege_back_when_it_enrols()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        keycloak.Stripped.ShouldContain(
            "kiosk-3",
            "a kiosk is born holding the realm's default privilege, so enrolling it must remove it");
    }

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
