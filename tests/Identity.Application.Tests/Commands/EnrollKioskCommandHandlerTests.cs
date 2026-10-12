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

    private static EnrollKioskCommand HappyCommand(string name = "3") =>
        new(
            name,
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
        keycloak.StripFailsFor.Add("kiosk.munich.3");
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
            "kiosk.munich.3",
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
        result.Value.ClientSecret.ShouldBe("secret-kiosk.munich.3");
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

    /// <summary>
    /// ADR-0172 — the handler, not the caller, composes the fab-qualified
    /// clientId (<c>kiosk.{fab}.{name}</c>). An absent name composes
    /// "kiosk.munich.", which a trailing dot does not fail
    /// <c>ClientId</c>'s own grammar — so the handler must refuse an absent
    /// name itself, before Keycloak or the repository are ever reached,
    /// rather than relying on <c>ClientId.From</c> to catch it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Absent_name_returns_InvalidName(string? name)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> result =
            await handler.HandleAsync(HappyCommand(name!), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<EnrollKioskError.InvalidName>();
        repo.Clients.ShouldBeEmpty();
        keycloak.Created.ShouldBeEmpty();
    }

    /// <summary>
    /// Mirrors <c>RegisterDeviceCommandHandlerTests</c>' identical segment-grammar rule
    /// (spec 323, issue #2627) — the kiosk name must be hyphen-separated alphanumeric
    /// segments, since it is composed straight into the clientId.
    /// </summary>
    [Theory]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("-x")]
    [InlineData("x-")]
    [InlineData("x--y")]
    [InlineData("x.y")]
    [InlineData("x_y")]
    public async Task A_name_that_is_not_hyphen_separated_alphanumeric_segments_returns_InvalidName(string name)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> result =
            await handler.HandleAsync(HappyCommand(name), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        EnrollKioskError.InvalidName error = result.Error.ShouldBeOfType<EnrollKioskError.InvalidName>();
        error.Reason.ShouldBe("each hyphen-separated segment must be one or more letters or digits.");
        repo.Clients.ShouldBeEmpty();
        keycloak.Created.ShouldBeEmpty();
    }

    /// <summary>
    /// ADR-0172 — the clientId is now fab-qualified (<c>kiosk.{fab}.{name}</c>), so two
    /// fabs enrolling a kiosk under the same name compose genuinely different strings
    /// and no longer collide. A same-fab repeat is still the identical clientId and
    /// must still conflict.
    /// </summary>
    [Fact]
    public async Task A_name_enrolled_by_another_fab_no_longer_conflicts_but_a_same_fab_repeat_still_does()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        EnrollKioskCommand munichCommand = HappyCommand();
        Result<KioskCredentialsDto, EnrollKioskError> munichAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);

        EnrollKioskCommand dresdenCommand = munichCommand with
        {
            Fab = FabIdentifier.From("dresden"),
            EnrolledBy = OperatorIdentifier.From(Guid.CreateVersion7()),
        };
        Result<KioskCredentialsDto, EnrollKioskError> dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);
        Result<KioskCredentialsDto, EnrollKioskError> munichSecondAttempt =
            await handler.HandleAsync(munichCommand, CancellationToken.None);

        munichAttempt.IsSuccess.ShouldBeTrue();
        dresdenAttempt.IsSuccess.ShouldBeTrue(
            "a cross-fab enrolment under the same name must now succeed — the two fabs compose "
            + "distinct, fab-qualified clientIds");
        dresdenAttempt.Value.ClientId.ShouldNotBe(munichAttempt.Value.ClientId);

        munichSecondAttempt.IsSuccess.ShouldBeFalse(
            "a same-fab repeat is still the identical clientId and must still conflict");
        munichSecondAttempt.Error.ShouldBeOfType<EnrollKioskError.KioskAlreadyEnrolled>();

        repo.Clients.Count.ShouldBe(2, "both fabs' enrolments must persist as distinct clients");
        keycloak.Created.Count.ShouldBe(2);
    }

    /// <summary>
    /// A legitimate multi-segment name — hyphen-separated alphanumeric
    /// segments, exactly what <c>IsHyphenSeparatedAlphanumeric</c> allows —
    /// must still enrol. The rejection-only coverage above could pass even if
    /// this validation had drifted into rejecting everything.
    /// </summary>
    [Fact]
    public async Task A_legitimate_multi_segment_name_is_accepted()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        Result<KioskCredentialsDto, EnrollKioskError> result =
            await handler.HandleAsync(HappyCommand("lobby-wall-2"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("kiosk.munich.lobby-wall-2");
        repo.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.Kiosk);
    }

    /// <summary>
    /// The counterfactual for the bug the <c>.</c> separator closes: a
    /// <c>-</c>-joined composition is not injective, because both
    /// <c>FabIdentifier</c> and the caller-supplied name may themselves
    /// contain <c>-</c>. <c>(fab: "dresden", name: "2-lobby")</c> and
    /// <c>(fab: "dresden-2", name: "lobby")</c> would both compose to
    /// <c>"kiosk-dresden-2-lobby"</c> under the old separator — one fab could
    /// squat or probe the other's client by shifting where the hyphen split
    /// falls. With <c>.</c> as the separator (neither a fab nor a name may
    /// contain one) the two compose to genuinely distinct strings and both
    /// enrolments must succeed independently.
    /// </summary>
    [Fact]
    public async Task A_hyphen_prefixed_fab_does_not_collide_with_a_shifted_name_split()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        EnrollKioskCommand dresdenCommand = new(
            "2-lobby", FabIdentifier.From("dresden"), OperatorIdentifier.From(Guid.CreateVersion7()));
        EnrollKioskCommand dresdenTwoCommand = new(
            "lobby", FabIdentifier.From("dresden-2"), OperatorIdentifier.From(Guid.CreateVersion7()));

        Result<KioskCredentialsDto, EnrollKioskError> dresdenAttempt =
            await handler.HandleAsync(dresdenCommand, CancellationToken.None);
        Result<KioskCredentialsDto, EnrollKioskError> dresdenTwoAttempt =
            await handler.HandleAsync(dresdenTwoCommand, CancellationToken.None);

        dresdenAttempt.IsSuccess.ShouldBeTrue();
        dresdenAttempt.Value.ClientId.ShouldBe("kiosk.dresden.2-lobby");
        dresdenTwoAttempt.IsSuccess.ShouldBeTrue(
            "a hyphen-prefixed fab must not collide with the first fab's shifted name split");
        dresdenTwoAttempt.Value.ClientId.ShouldBe("kiosk.dresden-2.lobby");
        dresdenTwoAttempt.Value.ClientId.ShouldNotBe(dresdenAttempt.Value.ClientId);

        repo.Clients.Count.ShouldBe(2, "both enrolments must persist as distinct clients");
    }
}
