using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 348, ADR-0144), captured green on the unmodified
/// <c>EnrollKioskCommandHandler</c> and <c>RegisterDeviceCommandHandler</c>
/// before their <c>HandleAsync</c> bodies are split into a validator, a
/// representation builder and a Keycloak call. Both create a Keycloak client
/// with a live secret, so what is pinned is the order the validations answer
/// in (first failing check wins, with its exact text), the exact
/// representation and fab group path Keycloak receives, the order the ports
/// are reached in, and the text of the success log.
///
/// <para>
/// Counterfactual this file was seen to catch: swapping the "must not be
/// empty" and "each hyphen-separated segment" checks in either handler turns
/// the empty-name and empty-identifier facts red; swapping the Keycloak
/// create and the repository add turns the success-sequence facts red.
/// </para>
/// </summary>
public class ClientRegistrationOrderCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000002", CultureInfo.InvariantCulture));

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private const string InvalidSegments =
        "each hyphen-separated segment must be one or more letters or digits.";

    private sealed class Harness
    {
        public Harness()
        {
            Keycloak = new RecordingKeycloakAdminClient(InnerKeycloak, Log);
            RecordingRegisteredClientRepository repository = new(Repository, Log);
            Enroll = new EnrollKioskCommandHandler(repository, Keycloak, new FakeClock(Now), EnrollLogger);
            Register = new RegisterDeviceCommandHandler(repository, Keycloak, new FakeClock(Now), RegisterLogger);
        }

        public List<string> Log { get; } = [];

        public InMemoryRegisteredClientRepository Repository { get; } = new();

        public FakeKeycloakAdminClient InnerKeycloak { get; } = new();

        public RecordingKeycloakAdminClient Keycloak { get; }

        public CapturingLogger<EnrollKioskCommandHandler> EnrollLogger { get; } = new();

        public CapturingLogger<RegisterDeviceCommandHandler> RegisterLogger { get; } = new();

        public EnrollKioskCommandHandler Enroll { get; }

        public RegisterDeviceCommandHandler Register { get; }

        public void SeedClient(string clientId, ClientKind kind, string fab) =>
            Repository.Seed(
                RegisteredClientAggregate.Register(
                    ClientId.From(clientId),
                    kind,
                    FabIdentifier.From(fab),
                    Operator,
                    new FakeClock(Now)));
    }

    private static EnrollKioskCommand Enrol(string name) => new(name, Munich, Operator);

    private static RegisterDeviceCommand RegisterCommand(string deviceType, string deviceIdentifier) =>
        new(deviceType, deviceIdentifier, Munich, Operator);

    // ---- kiosk enrolment ------------------------------------------------------

    [Fact]
    public async Task An_empty_kiosk_name_is_refused_with_the_must_not_be_empty_text_and_no_port_is_called()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(Enrol(string.Empty), CancellationToken.None);

        result.Error.ShouldBe(EnrollKioskFailures.InvalidName("must not be empty."));
        result.Error.Message.ShouldBe("name rejected: must not be empty.");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_whitespace_kiosk_name_is_refused_as_empty_not_as_a_bad_segment()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(Enrol("   "), CancellationToken.None);

        result.Error.Message.ShouldBe("name rejected: must not be empty.");
    }

    [Fact]
    public async Task A_kiosk_name_with_a_bad_segment_is_refused_with_the_segment_text()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(Enrol("press--1"), CancellationToken.None);

        result.Error.Code.ShouldBe("KIOSK_INVALID_NAME");
        result.Error.Message.ShouldBe($"name rejected: {InvalidSegments}");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_kiosk_name_that_is_both_too_long_and_badly_segmented_is_refused_as_badly_segmented()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(
            Enrol(new string('a', 300) + "!"), CancellationToken.None);

        result.Error.Message.ShouldBe($"name rejected: {InvalidSegments}");
    }

    [Fact]
    public async Task A_kiosk_name_that_only_makes_the_client_id_too_long_is_refused_with_the_client_id_text()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(
            Enrol(new string('a', 300)), CancellationToken.None);

        result.Error.Code.ShouldBe("KIOSK_INVALID_NAME");
        result.Error.Message.ShouldBe(
            "name rejected: value must be no more than 255 character(s). (Parameter 'value')");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_already_enrolled_kiosk_is_refused_after_the_fab_scoped_lookup_alone()
    {
        Harness harness = new();
        harness.SeedClient("kiosk.munich.press-1", ClientKind.Kiosk, "munich");

        var result = await harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None);

        result.Error.ShouldBe(EnrollKioskFailures.KioskAlreadyEnrolled("kiosk.munich.press-1"));
        result.Error.Message.ShouldBe("A kiosk with clientId 'kiosk.munich.press-1' is already enrolled.");
        harness.Log.ShouldBe(["lookup"]);
    }

    [Fact]
    public async Task A_kiosk_enrolment_looks_up_then_creates_in_keycloak_then_adds_then_saves()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None);

        harness.Log.ShouldBe(["lookup", "create", "add", "save"]);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("kiosk.munich.press-1");
        result.Value.Fab.ShouldBe("munich");
        result.Value.ClientSecret.ShouldBe("secret-kiosk.munich.press-1");
        harness.Repository.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.Kiosk);
        result.Value.RegisteredClientIdentifier.ShouldBe(harness.Repository.Clients.Single().Id.Value);
    }

    [Fact]
    public async Task A_kiosk_enrolment_sends_keycloak_exactly_this_representation_and_fab_group_path()
    {
        Harness harness = new();

        await harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None);

        var (representation, fabGroupPath) = harness.Keycloak.CreateRequests.ShouldHaveSingleItem();
        fabGroupPath.ShouldBe("/fabs/munich");
        representation.ClientId.ShouldBe("kiosk.munich.press-1");
        representation.Name.ShouldBe("Kiosk kiosk.munich.press-1");
        representation.ServiceAccountsEnabled.ShouldBeTrue();
        representation.StandardFlowEnabled.ShouldBeFalse();
        representation.DirectAccessGrantsEnabled.ShouldBeFalse();
        representation.PublicClient.ShouldBeFalse();
        representation.DefaultClientScopes.ShouldBe(
            [
                "sse.cameras.read",
                "sse.streams.read",
                "sse.layouts.read",
                "sse.overlays.read",
                "sse.variables.read",
                "sse.events.write",
                "sse-audience",
                "sse-groups",
            ]);
        representation.OptionalClientScopes.ShouldBeEmpty();
        representation.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray().ShouldBe(
            [
                new KeyValuePair<string, string>("sse.fab", "munich"),
                new KeyValuePair<string, string>("sse.kind", "kiosk"),
            ]);
    }

    [Fact]
    public async Task A_keycloak_conflict_on_kiosk_enrolment_names_the_client_id_the_exception_carries()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new KeycloakClientAlreadyExistsException("kiosk.dresden.other");

        var result = await harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None);

        result.Error.ShouldBe(EnrollKioskFailures.KioskAlreadyEnrolled("kiosk.dresden.other"));
        harness.Log.ShouldBe(["lookup", "create"]);
    }

    [Fact]
    public async Task Any_other_keycloak_failure_on_kiosk_enrolment_answers_unavailable_and_adds_and_saves_nothing()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new InvalidOperationException("realm down");

        var result = await harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None);

        result.Error.Code.ShouldBe("KEYCLOAK_UNAVAILABLE");
        result.Error.Message.ShouldBe("Keycloak Admin API call failed: realm down");
        harness.Log.ShouldBe(["lookup", "create"]);
        harness.Repository.Clients.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancellation_from_keycloak_on_kiosk_enrolment_propagates()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new OperationCanceledException("cancelled");

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None));

        // An OperationCanceledException leaves an async method as a cancelled task: its type survives the await, its message does not.
        thrown.ShouldNotBeNull();
        harness.Log.ShouldBe(["lookup", "create"]);
    }

    [Fact]
    public async Task An_enrolled_kiosk_is_logged_with_this_exact_text()
    {
        Harness harness = new();

        var result = await harness.Enroll.HandleAsync(Enrol("press-1"), CancellationToken.None);

        var entry = harness.EnrollLogger.Named("EnrolledKiosk").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe(
            $"Enrolled kiosk {result.Value.RegisteredClientIdentifier} 'kiosk.munich.press-1' for fab munich.");
        entry.Field("ClientId").ShouldBe("kiosk.munich.press-1");
        entry.Field("Fab").ShouldBe("munich");
    }

    // ---- device registration --------------------------------------------------

    [Fact]
    public async Task A_device_with_a_bad_type_and_a_bad_identifier_is_refused_as_a_bad_type_first()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(RegisterCommand("robot", string.Empty), CancellationToken.None);

        result.Error.ShouldBe(RegisterDeviceFailures.InvalidDeviceType("robot"));
        result.Error.Message.ShouldBe("deviceType 'robot' is not allowed; expected: plc | inference.");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_device_identifier_is_refused_with_the_must_not_be_empty_text()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", string.Empty), CancellationToken.None);

        result.Error.Code.ShouldBe("DEVICE_INVALID_IDENTIFIER");
        result.Error.Message.ShouldBe("deviceIdentifier rejected: must not be empty.");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_device_identifier_with_a_bad_segment_is_refused_with_the_segment_text()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", "oven--12"), CancellationToken.None);

        result.Error.Message.ShouldBe($"deviceIdentifier rejected: {InvalidSegments}");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_device_identifier_that_is_both_too_long_and_badly_segmented_is_refused_as_badly_segmented()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(
            RegisterCommand("plc", new string('a', 300) + "!"), CancellationToken.None);

        result.Error.Message.ShouldBe($"deviceIdentifier rejected: {InvalidSegments}");
    }

    [Fact]
    public async Task A_device_identifier_that_only_makes_the_client_id_too_long_is_refused_with_the_client_id_text()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(
            RegisterCommand("inference", new string('a', 300)), CancellationToken.None);

        result.Error.Code.ShouldBe("DEVICE_INVALID_IDENTIFIER");
        result.Error.Message.ShouldBe(
            "deviceIdentifier rejected: value must be no more than 255 character(s). (Parameter 'value')");
        harness.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_already_registered_device_is_refused_after_the_fab_scoped_lookup_alone()
    {
        Harness harness = new();
        harness.SeedClient("plc.munich.oven-12", ClientKind.Device, "munich");

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", "oven-12"), CancellationToken.None);

        result.Error.ShouldBe(RegisterDeviceFailures.DeviceAlreadyRegistered("plc.munich.oven-12"));
        result.Error.Message.ShouldBe("A device with clientId 'plc.munich.oven-12' is already registered.");
        harness.Log.ShouldBe(["lookup"]);
    }

    [Fact]
    public async Task A_device_registration_looks_up_then_creates_in_keycloak_then_adds_then_saves()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", "oven-12"), CancellationToken.None);

        harness.Log.ShouldBe(["lookup", "create", "add", "save"]);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("plc.munich.oven-12");
        result.Value.DeviceType.ShouldBe("plc");
        result.Value.DeviceIdentifier.ShouldBe("oven-12");
        result.Value.Fab.ShouldBe("munich");
        result.Value.ClientSecret.ShouldBe("secret-plc.munich.oven-12");
        harness.Repository.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.Device);
        result.Value.RegisteredClientIdentifier.ShouldBe(harness.Repository.Clients.Single().Id.Value);
    }

    [Fact]
    public async Task A_device_registration_sends_keycloak_exactly_this_representation_and_fab_group_path()
    {
        Harness harness = new();

        await harness.Register.HandleAsync(RegisterCommand("inference", "gpu-3"), CancellationToken.None);

        var (representation, fabGroupPath) = harness.Keycloak.CreateRequests.ShouldHaveSingleItem();
        fabGroupPath.ShouldBe("/fabs/munich");
        representation.ClientId.ShouldBe("inference.munich.gpu-3");
        representation.Name.ShouldBe("inference gpu-3");
        representation.ServiceAccountsEnabled.ShouldBeTrue();
        representation.StandardFlowEnabled.ShouldBeFalse();
        representation.DirectAccessGrantsEnabled.ShouldBeFalse();
        representation.PublicClient.ShouldBeFalse();
        representation.DefaultClientScopes.ShouldBe(
            ["sse.cameras.read", "sse.events.publish", "sse-audience", "sse-groups"]);
        representation.OptionalClientScopes.ShouldBeEmpty();
        representation.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray().ShouldBe(
            [
                new KeyValuePair<string, string>("sse.deviceIdentifier", "gpu-3"),
                new KeyValuePair<string, string>("sse.deviceType", "inference"),
                new KeyValuePair<string, string>("sse.fab", "munich"),
                new KeyValuePair<string, string>("sse.kind", "device"),
            ]);
    }

    [Fact]
    public async Task A_keycloak_conflict_on_device_registration_names_the_client_id_the_exception_carries()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new KeycloakClientAlreadyExistsException("plc.dresden.other");

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", "oven-12"), CancellationToken.None);

        result.Error.ShouldBe(RegisterDeviceFailures.DeviceAlreadyRegistered("plc.dresden.other"));
        harness.Log.ShouldBe(["lookup", "create"]);
    }

    [Fact]
    public async Task Any_other_keycloak_failure_on_device_registration_answers_unavailable_and_adds_and_saves_nothing()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new InvalidOperationException("realm down");

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", "oven-12"), CancellationToken.None);

        result.Error.Code.ShouldBe("KEYCLOAK_UNAVAILABLE");
        result.Error.Message.ShouldBe("Keycloak Admin API call failed: realm down");
        harness.Log.ShouldBe(["lookup", "create"]);
        harness.Repository.Clients.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancellation_from_keycloak_on_device_registration_propagates()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new OperationCanceledException("cancelled");

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Register.HandleAsync(RegisterCommand("plc", "oven-12"), CancellationToken.None));

        // An OperationCanceledException leaves an async method as a cancelled task: its type survives the await, its message does not.
        thrown.ShouldNotBeNull();
        harness.Log.ShouldBe(["lookup", "create"]);
    }

    [Fact]
    public async Task A_registered_device_is_logged_with_this_exact_text()
    {
        Harness harness = new();

        var result = await harness.Register.HandleAsync(RegisterCommand("plc", "oven-12"), CancellationToken.None);

        var entry = harness.RegisterLogger.Named("RegisteredDevice").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe(
            $"Registered device {result.Value.RegisteredClientIdentifier} 'plc.munich.oven-12' (plc/oven-12) for fab munich.");
        entry.Field("ClientId").ShouldBe("plc.munich.oven-12");
        entry.Field("DeviceType").ShouldBe("plc");
        entry.Field("DeviceIdentifier").ShouldBe("oven-12");
        entry.Field("Fab").ShouldBe("munich");
    }
}
