using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
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

        var result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("plc.munich.station-4");
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
        var second =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        second.IsSuccess.ShouldBeFalse();
        second.Error.ShouldBeOfType<RegisterDeviceError.DeviceAlreadyRegistered>();
    }

    /// <summary>
    /// ADR-0172 — the clientId is now fab-qualified
    /// (<c>plc.{fab}.{identifier}</c>), so two fabs registering the same
    /// device identifier compose genuinely different strings and the
    /// lookup (now <c>GetWithinFabAsync</c>, fab-scoped as defense in depth)
    /// no longer sees a collision regardless. A same-fab repeat is still the
    /// identical clientId and must still conflict — the fix narrows the
    /// uniqueness check to within a fab, it does not remove it.
    /// </summary>
    [Fact]
    public async Task A_clientId_registered_by_another_fab_no_longer_conflicts_but_a_same_fab_repeat_still_does()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        var munichCommand = HappyCommand();
        var munichAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);

        var dresdenCommand = munichCommand with
        {
            Fab = FabIdentifier.From("dresden"),
            RegisteredBy = OperatorIdentifier.From(Guid.CreateVersion7()),
        };
        var dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);
        var munichSecondAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);

        munichAttempt.IsSuccess.ShouldBeTrue();
        dresdenAttempt.IsSuccess.ShouldBeTrue(
            "a cross-fab registration of the same device identifier must now succeed — "
            + "the two fabs compose distinct, fab-qualified clientIds");
        dresdenAttempt.Value.ClientId.ShouldNotBe(munichAttempt.Value.ClientId);

        munichSecondAttempt.IsSuccess.ShouldBeFalse(
            "a same-fab repeat is still the identical clientId and must still conflict");
        munichSecondAttempt.Error.ShouldBeOfType<RegisterDeviceError.DeviceAlreadyRegistered>();

        repo.Clients.Count.ShouldBe(2, "both fabs' registrations must persist as distinct clients");
        keycloak.Created.Count.ShouldBe(2);
    }

    /// <summary>
    /// Exercises the *other* conflict branch
    /// (<c>RegisterDeviceCommandHandler.cs</c>'s <c>KeycloakClientAlreadyExistsException</c>
    /// catch), which the fact above cannot reach: every DB-backed scenario
    /// leaves a row for the conflicting clientId, so the repository lookup
    /// always wins the race and the Keycloak catch never runs. Here the
    /// database has no row at all — only Keycloak holds munich's
    /// fab-qualified clientId, seeded directly via
    /// <see cref="FakeKeycloakAdminClient.CreateClientAsync"/> without going
    /// through the repository, modelling "created in Keycloak, DB write
    /// never landed" or any other state where the two diverge. A same-fab
    /// repeat composes the identical string and must still hit this branch;
    /// a cross-fab attempt composes a different one (ADR-0172) and must not.
    /// </summary>
    [Fact]
    public async Task A_clientId_held_only_in_Keycloak_still_refuses_the_same_fab_but_not_another()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        // Keycloak already holds "plc.munich.station-4"; the database has no
        // row for it at all, so the repository lookup cannot be the branch
        // that catches this conflict.
        await keycloak.CreateClientAsync(
            new KeycloakClientRepresentation(
                ClientId: "plc.munich.station-4",
                Name: "plc munich-station-4",
                ServiceAccountsEnabled: true,
                StandardFlowEnabled: false,
                DirectAccessGrantsEnabled: false,
                PublicClient: false,
                DefaultClientScopes: [],
                OptionalClientScopes: [],
                Attributes: new Dictionary<string, string> { ["sse.fab"] = "munich" }),
            fabGroupPath: "/fabs/munich",
            CancellationToken.None);

        var munichCommand = HappyCommand();
        var dresdenCommand = munichCommand with
        {
            Fab = FabIdentifier.From("dresden"),
            RegisteredBy = OperatorIdentifier.From(Guid.CreateVersion7()),
        };

        var munichAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);
        var dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);

        munichAttempt.IsSuccess.ShouldBeFalse(
            "munich's attempt composes the exact clientId Keycloak already holds");
        munichAttempt.Error.ShouldBe(RegisterDeviceFailures.DeviceAlreadyRegistered("plc.munich.station-4"));

        dresdenAttempt.IsSuccess.ShouldBeTrue(
            "dresden composes a different, fab-qualified clientId — ADR-0172's Keycloak-only conflict "
            + "branch must no longer catch another fab's attempt");
        dresdenAttempt.Value.ClientId.ShouldBe("plc.dresden.station-4");

        repo.Clients.ShouldHaveSingleItem("only dresden's attempt may persist to the database");
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

        var result =
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
        var result =
            await handler.HandleAsync(
                HappyCommand(deviceIdentifier: "has space"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceIdentifier>();
    }

    /// <summary>
    /// A null or empty deviceIdentifier would otherwise pass the ClientId
    /// grammar (it composes "plc.munich." or "inference.munich.", both
    /// valid), so the handler must refuse it before Keycloak or the
    /// repository are ever reached. No client may be created for any of
    /// these rows.
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

        var result =
            await handler.HandleAsync(
                HappyCommand(deviceType: deviceType, deviceIdentifier: deviceIdentifier!),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceIdentifier>();
        repo.Clients.ShouldBeEmpty();
        keycloak.Created.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 323 — the composed clientId is <c>&lt;type&gt;.&lt;fab&gt;.&lt;deviceIdentifier&gt;</c>
    /// (ADR-0172 added the fab segment), so a deviceIdentifier of "-" composes "plc.munich.-" and
    /// "." composes "plc.munich.." — the two examples named in issue #2627. Every row must be
    /// refused on the new segment-grammar reason, not
    /// merely refused for some other reason (e.g. the ClientId grammar), so that " x" — which
    /// already failed today via ClientId's own message — is confirmed to fail on the new
    /// reason instead.
    /// </summary>
    [Theory]
    [InlineData("plc", "-")]
    [InlineData("plc", ".")]
    [InlineData("plc", "-x")]
    [InlineData("plc", "x-")]
    [InlineData("plc", "x--y")]
    [InlineData("plc", "x.y")]
    [InlineData("plc", "x_y")]
    [InlineData("plc", "line-3.cell_2")]
    [InlineData("plc", " x")]
    [InlineData("inference", "a_b")]
    public async Task An_identifier_that_is_not_hyphen_separated_alphanumeric_segments_returns_InvalidDeviceIdentifier(
        string deviceType, string deviceIdentifier)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                HappyCommand(deviceType: deviceType, deviceIdentifier: deviceIdentifier),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        var error =
            result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceIdentifier>();
        error.Reason.ShouldBe("each hyphen-separated segment must be one or more letters or digits.");
        repo.Clients.ShouldBeEmpty();
        keycloak.Created.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 323 green-guard — confirms the new segment-grammar rule does not reject any
    /// identifier that is, and must remain, hyphen-separated alphanumeric segments.
    /// </summary>
    [Theory]
    [InlineData("x")]
    [InlineData("4station")]
    [InlineData("station-4")]
    [InlineData("line-3-cell-2")]
    public async Task A_hyphen_separated_alphanumeric_identifier_registers(string deviceIdentifier)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                HappyCommand(deviceIdentifier: deviceIdentifier), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("plc.munich." + deviceIdentifier);
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "Keycloak timeout" };
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.KeycloakUnavailable>();
    }

    /// <summary>
    /// The counterfactual for the bug the <c>.</c> separator closes: a
    /// <c>-</c>-joined composition is not injective, because both
    /// <c>FabIdentifier</c> and the caller-supplied deviceIdentifier may
    /// themselves contain <c>-</c>. <c>(fab: "dresden", identifier:
    /// "2-press")</c> and <c>(fab: "dresden-2", identifier: "press")</c>
    /// would both compose to <c>"plc-dresden-2-press"</c> under the old
    /// separator — one fab could squat or probe the other's device by
    /// shifting where the hyphen split falls. With <c>.</c> as the separator
    /// the two compose to genuinely distinct strings and both registrations
    /// must succeed independently.
    /// </summary>
    [Fact]
    public async Task A_hyphen_prefixed_fab_does_not_collide_with_a_shifted_identifier_split()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        RegisterDeviceCommand dresdenCommand = new(
            "plc", "2-press", FabIdentifier.From("dresden"), OperatorIdentifier.From(Guid.CreateVersion7()));
        RegisterDeviceCommand dresdenTwoCommand = new(
            "plc", "press", FabIdentifier.From("dresden-2"), OperatorIdentifier.From(Guid.CreateVersion7()));

        var dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);
        var dresdenTwoAttempt =
            await handler.HandleAsync(dresdenTwoCommand, CancellationToken.None);

        dresdenAttempt.IsSuccess.ShouldBeTrue();
        dresdenAttempt.Value.ClientId.ShouldBe("plc.dresden.2-press");
        dresdenTwoAttempt.IsSuccess.ShouldBeTrue(
            "a hyphen-prefixed fab must not collide with the first fab's shifted identifier split");
        dresdenTwoAttempt.Value.ClientId.ShouldBe("plc.dresden-2.press");
        dresdenTwoAttempt.Value.ClientId.ShouldNotBe(dresdenAttempt.Value.ClientId);

        repo.Clients.Count.ShouldBe(2, "both registrations must persist as distinct clients");
    }
}
