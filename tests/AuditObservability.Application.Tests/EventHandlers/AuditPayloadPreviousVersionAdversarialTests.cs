using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Spec 403 (#3008), adversarial pass on the audit consumer: <c>CameraAddressChangedV1</c> gained a trailing
/// <c>PreviousVersion</c>. The redaction rebuilds the record with <c>with { ... }</c>, so the new member must
/// travel into the stored payload untouched while both addresses are still reduced to their origin and the
/// appended <c>DetailChanged</c> member still lands inside a well-formed object. The real handler runs over the
/// in-memory repository.
/// </summary>
public class AuditPayloadPreviousVersionAdversarialTests
{
    private const string Marker = "SECRET";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-14T09:00:10Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public async Task A_versioned_change_is_stored_with_its_version_and_both_addresses_reduced_to_their_origin(int version)
    {
        var json = await StoredAsync(version);

        json.GetProperty("PreviousVersion").GetInt32().ShouldBe(version);
        json.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://10.0.8.1:8554");
        json.GetProperty("Url").GetString().ShouldBe("rtsp://10.0.8.2:8554");
        json.GetProperty("DetailChanged").ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        json.GetRawText().ShouldNotContain(Marker);
    }

    [Fact]
    public async Task A_version_less_change_is_stored_with_an_explicit_null_and_stays_well_formed()
    {
        var json = await StoredAsync(null);

        json.GetProperty("PreviousVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("DetailChanged").ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        json.GetRawText().ShouldNotContain(Marker);
    }

    [Fact]
    public void The_version_is_not_registered_as_a_url_field_and_the_url_register_is_unchanged()
    {
        AuditPayloadRedaction.UrlFields[typeof(CameraAddressChangedV1)]
            .ShouldBe([nameof(CameraAddressChangedV1.PreviousUrl), nameof(CameraAddressChangedV1.Url)]);
    }

    private static async Task<JsonElement> StoredAsync(int? version)
    {
        InMemoryAuditEventRepository repository = new();
        IntegrationEventAuditHandler handler = new(new AuditingMessageHandler(
            repository,
            V1ResourceMap.Default,
            new FakeClock(Now),
            Options.Create(new AuditMeasurementOptions()),
            NullLogger<AuditingMessageHandler>.Instance));

        CameraAddressChangedV1 message = new(
            Guid.CreateVersion7(),
            "munich",
            $"rtsp://admin:{Marker}@10.0.8.1:8554/{Marker}-old?token={Marker}",
            $"rtsp://10.0.8.2:8554/{Marker}-new?token={Marker}",
            OccurredAt,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.CreateVersion7(), OccurredAt, Fab: "munich", Actor: null),
            PreviousVersion: version);

        await handler.Handle(message, CancellationToken.None);

        var raw = repository.Committed.ShouldHaveSingleItem().Payload.Content.Value;
        return JsonDocument.Parse(raw).RootElement.Clone();
    }
}
