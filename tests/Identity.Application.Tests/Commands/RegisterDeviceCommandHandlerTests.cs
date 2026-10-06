using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

public class RegisterDeviceCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static RegisterDeviceCommand HappyCommand(
        string deviceType = "plc", string deviceIdentifier = "station-4") =>
        new(
            deviceType,
            deviceIdentifier,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()));

    [Fact]
    public async Task Happy_path_creates_a_Keycloak_client_with_devicetype_deviceid_clientId()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("plc-station-4");
        result.Value.DeviceType.ShouldBe("plc");
        result.Value.DeviceIdentifier.ShouldBe("station-4");
        repo.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.Device);
    }

    [Fact]
    public async Task Re_registration_returns_DeviceAlreadyRegistered()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand(), CancellationToken.None);
        Result<DeviceCredentialsDto, RegisterDeviceError> second =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        second.IsSuccess.ShouldBeFalse();
        second.Error.ShouldBeOfType<RegisterDeviceError.DeviceAlreadyRegistered>();
    }

    /// <summary>
    /// Spec 304 US1 — the 409 is already fab-neutral; this pins it. Whether
    /// the conflicting registration is attempted from the holder's own fab or
    /// from another fab entirely, <c>GetByClientIdAsync</c> is the same
    /// realm-global lookup (spec plan 304 §1), so both refusals must carry
    /// the identical <see cref="RegisterDeviceError.DeviceAlreadyRegistered"/>
    /// value and neither attempt may create anything.
    /// </summary>
    [Fact]
    public async Task A_clientId_held_by_another_fab_is_refused_with_the_same_error_as_one_held_by_the_callers_fab()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        RegisterDeviceCommand munichCommand = HappyCommand();
        await handler.HandleAsync(munichCommand, CancellationToken.None);

        RegisterDeviceCommand dresdenCommand = munichCommand with
        {
            Fab = FabIdentifier.From("dresden"),
            RegisteredBy = OperatorIdentifier.From(Guid.CreateVersion7()),
        };
        Result<DeviceCredentialsDto, RegisterDeviceError> dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);
        Result<DeviceCredentialsDto, RegisterDeviceError> munichSecondAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);

        dresdenAttempt.IsSuccess.ShouldBeFalse();
        munichSecondAttempt.IsSuccess.ShouldBeFalse();
        dresdenAttempt.Error.ShouldBe(
            munichSecondAttempt.Error,
            "a cross-fab conflict must answer exactly what a same-fab conflict answers — "
            + "nothing in the response may vary by which fab actually holds the clientId");
        repo.Clients.ShouldHaveSingleItem().Fab.ShouldBe(FabIdentifier.From("munich"));
        keycloak.Created.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Spec 304 US1 — exercises the *other* conflict branch
    /// (<c>RegisterDeviceCommandHandler.cs:79-82</c>), which the fact above
    /// cannot reach: every DB-backed scenario leaves a row for the
    /// conflicting clientId, so <c>GetByClientIdAsync</c> (:46-51) always
    /// wins the race and the Keycloak catch never runs. Here the database
    /// has no row at all — only Keycloak holds the clientId, seeded directly
    /// via <see cref="FakeKeycloakAdminClient.CreateClientAsync"/> without
    /// going through the repository, modelling "created in Keycloak, DB
    /// write never landed" or any other state where the two diverge. Both a
    /// same-fab and a cross-fab attempt against that Keycloak-only conflict
    /// must throw <see cref="KeycloakClientAlreadyExistsException"/> carrying
    /// only the caller's own clientId (never the holder's), so the two
    /// resulting errors must be record-equal and neither may mention the
    /// holder's fab.
    /// </summary>
    [Fact]
    public async Task A_clientId_held_only_in_Keycloak_is_refused_with_the_same_holder_neutral_error_regardless_of_the_callers_fab()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        // Keycloak already holds "plc-station-4" under munich; the database
        // has no row for it at all, so GetByClientIdAsync cannot be the
        // branch that catches this conflict.
        await keycloak.CreateClientAsync(
            new KeycloakClientRepresentation(
                ClientId: "plc-station-4",
                Name: "plc station-4",
                ServiceAccountsEnabled: true,
                StandardFlowEnabled: false,
                DirectAccessGrantsEnabled: false,
                PublicClient: false,
                DefaultClientScopes: [],
                OptionalClientScopes: [],
                Attributes: new Dictionary<string, string> { ["sse.fab"] = "munich" }),
            fabGroupPath: "/fabs/munich",
            CancellationToken.None);

        RegisterDeviceCommand munichCommand = HappyCommand();
        RegisterDeviceCommand dresdenCommand = munichCommand with
        {
            Fab = FabIdentifier.From("dresden"),
            RegisteredBy = OperatorIdentifier.From(Guid.CreateVersion7()),
        };

        Result<DeviceCredentialsDto, RegisterDeviceError> munichAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);
        Result<DeviceCredentialsDto, RegisterDeviceError> dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);

        munichAttempt.IsSuccess.ShouldBeFalse();
        dresdenAttempt.IsSuccess.ShouldBeFalse();
        munichAttempt.Error.ShouldBeOfType<RegisterDeviceError.DeviceAlreadyRegistered>();
        dresdenAttempt.Error.ShouldBe(
            munichAttempt.Error,
            "a cross-fab attempt against a Keycloak-only conflict must answer exactly what "
            + "a same-fab attempt against the same conflict answers");
        dresdenAttempt.Error.ShouldBe(RegisterDeviceFailures.DeviceAlreadyRegistered("plc-station-4"));

        repo.Clients.ShouldBeEmpty("neither attempt may persist anything to the database");

        string dresdenMessage = ((RegisterDeviceError.DeviceAlreadyRegistered)dresdenAttempt.Error).Message;
        string munichMessage = ((RegisterDeviceError.DeviceAlreadyRegistered)munichAttempt.Error).Message;
        dresdenMessage.ShouldNotContain(
            "munich", customMessage: $"the error must never name the holder's fab — got: {dresdenMessage}");
        munichMessage.ShouldNotContain(
            "munich", customMessage: $"the error must never name the holder's fab — got: {munichMessage}");
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("webhook")]
    [InlineData("not-a-source")]
    public async Task Invalid_device_type_returns_InvalidDeviceType(string badType)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(HappyCommand(deviceType: badType), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceType>();
        repo.Clients.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_device_identifier_returns_InvalidDeviceIdentifier()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        // A deviceIdentifier containing whitespace yields a clientId
        // 'plc-has space' that fails the ClientId grammar.
        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(
                HappyCommand(deviceIdentifier: "has space"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceIdentifier>();
    }

    /// <summary>
    /// A null or empty deviceIdentifier would otherwise pass the ClientId
    /// grammar (it composes "plc-" or "inference-", both valid), so the
    /// handler must refuse it before Keycloak or the repository are ever
    /// reached. No client may be created for any of these rows.
    /// </summary>
    [Theory]
    [InlineData("plc", null)]
    [InlineData("plc", "")]
    [InlineData("inference", "")]
    public async Task Absent_device_identifier_returns_InvalidDeviceIdentifier(
        string deviceType, string? deviceIdentifier)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(
                HappyCommand(deviceType: deviceType, deviceIdentifier: deviceIdentifier!),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceIdentifier>();
        repo.Clients.ShouldBeEmpty();
        keycloak.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "Keycloak timeout" };
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.KeycloakUnavailable>();
    }
}
