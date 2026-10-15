using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Spec 377 (#2977) adversarial guards around the two message types that carry a camera URL. Each is green
/// on the code before the redaction and must stay green after it.
///
/// <para>
/// <b>Spelling of the other fields.</b> Plan §2.1 and §2.2 promise "unchanged keys, value reduced" for a
/// redacted type. A message whose URL is already an origin has nothing to reduce, so its stored payload
/// must still be what the serializer writes, byte for byte. A <c>JsonNode</c> round-trip does not do that:
/// it re-escapes the <c>+</c> of every <c>DateTimeOffset</c> offset as <c>\u002B</c>, which changes
/// <c>PayloadSizeBytes</c> without changing the content. The stored column is <c>jsonb</c>, so byte identity
/// is a property of the string and of <c>PayloadSizeBytes</c>, not of what is on disk.
/// </para>
///
/// <para>
/// <b>Second sinks.</b> The URL must reach no row column other than the payload; and a failure inside the
/// audit write must surface to the bus rather than be swallowed, because fail-closed redaction relies on
/// Wolverine seeing the exception.
/// </para>
/// </summary>
public class AuditPayloadRedactionAdversarialTests
{
    private const string Marker = "SECRET";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-30T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-05-30T08:00:00.1234567+02:00", CultureInfo.InvariantCulture);

    private static IntegrationEventAuditHandler Build(IAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));

    private static EventMetadata Metadata() =>
        new(Guid.CreateVersion7(), OccurredAt, Fab: "munich", Actor: Guid.CreateVersion7(), RootIngestedAt: OccurredAt);

    [Fact]
    public async Task A_registration_whose_url_is_already_an_origin_is_stored_exactly_as_the_serializer_writes_it()
    {
        InMemoryAuditEventRepository repo = new();
        CameraRegisteredV1 message = new(
            Guid.CreateVersion7(), "north \"gate\" <&+> café", "rtsp://cam", OccurredAt, Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        repo.Committed[0].Payload.Content.Value.ShouldBe(JsonSerializer.Serialize(message, message.GetType()));
    }

    [Fact]
    public async Task An_address_change_between_origins_keeps_every_existing_field_spelled_as_the_serializer_writes_it()
    {
        InMemoryAuditEventRepository repo = new();
        CameraAddressChangedV1 message = new(
            Guid.CreateVersion7(), "munich", "rtsp://10.0.5.12", "rtsp://10.0.5.44", OccurredAt,
            Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        var serialized = JsonSerializer.Serialize(message, message.GetType());
        repo.Committed[0].Payload.Content.Value.ShouldStartWith(serialized[..^1]);
    }

    [Fact]
    public async Task A_registration_keeps_a_url_shaped_camera_name_verbatim()
    {
        InMemoryAuditEventRepository repo = new();
        const string name = "rtsp://cam/name?token=KEEP";
        CameraRegisteredV1 message = new(
            Guid.CreateVersion7(), name, "rtsp://cam", OccurredAt, Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        JsonDocument.Parse(repo.Committed[0].Payload.Content.Value).RootElement
            .GetProperty("Name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task No_audit_row_column_other_than_the_payload_carries_the_registered_url()
    {
        InMemoryAuditEventRepository repo = new();
        var camera = Guid.CreateVersion7();
        CameraRegisteredV1 message = new(
            camera, "north-gate", "rtsp://cam/stream?token=SECRET", OccurredAt, Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        var row = repo.Committed[0];
        row.ResourceIdentifier!.Value.ShouldBe(camera.ToString());
        row.EventKind.Value.ShouldBe(nameof(CameraRegisteredV1));
        NonPayloadStrings(AuditRowMapper.Map(row)).ShouldAllBe(value => !value.Contains(Marker));
    }

    [Fact]
    public async Task No_audit_row_column_other_than_the_payload_carries_either_changed_url()
    {
        InMemoryAuditEventRepository repo = new();
        var camera = Guid.CreateVersion7();
        CameraAddressChangedV1 message = new(
            camera, "munich", "rtsp://cam/a?token=SECRETOLD", "rtsp://cam/b?token=SECRETNEW", OccurredAt,
            Guid.CreateVersion7(), Metadata());

        await Build(repo).Handle(message, default);

        var row = repo.Committed[0];
        row.ResourceIdentifier!.Value.ShouldBe(camera.ToString());
        NonPayloadStrings(AuditRowMapper.Map(row)).ShouldAllBe(value => !value.Contains(Marker));
    }

    [Fact]
    public async Task A_failure_while_writing_the_audit_row_reaches_the_bus_instead_of_being_swallowed()
    {
        CameraRegisteredV1 message = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://cam/stream?token=SECRET", OccurredAt,
            Guid.CreateVersion7(), Metadata());

        await Should.ThrowAsync<InvalidOperationException>(
            () => Build(new FailingRepository()).Handle(message, default));
    }

    private static IEnumerable<string> NonPayloadStrings(object dto) =>
        dto.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "Payload")
            .Select(property => property.GetValue(dto)?.ToString() ?? string.Empty);

    private sealed class FailingRepository : IAuditEventRepository
    {
        public void Add(AuditEventEntity audit)
        {
        }

        public Task SaveAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit store unavailable");
    }
}
