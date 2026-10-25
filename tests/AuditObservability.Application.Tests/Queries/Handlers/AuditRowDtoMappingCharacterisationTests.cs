using System.Globalization;
using System.Text;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

// Spec 459 (#3154), phase 4a: characterises every AuditRowDto field on the mapping as it
// stands, so the move into an extension method is proven behaviour-preserving. Each field
// carries a value distinct from every same-typed sibling.
public class AuditRowDtoMappingCharacterisationTests
{
    [Fact]
    public async Task Every_field_of_the_row_is_mapped_from_its_own_source()
    {
        var actor = Guid.Parse("aaaaaaaa-0000-7000-8000-000000000001");
        var eventIdentifier = Guid.Parse("eeeeeeee-0000-7000-8000-000000000002");
        var occurredAt = DateTimeOffset.Parse("2026-03-04T05:06:07Z", CultureInfo.InvariantCulture);
        const string payload = """{"cameraIdentifier":"44444444-4444-4444-4444-444444444444","extra":"ü"}""";
        const string resourceIdentifier = "44444444-4444-4444-4444-444444444444";

        // The builder pins ReceivedAt at 2026-05-29T08:14:34Z, distinct from the OccurredAt chosen here.
        var row = new AuditEventBuilder()
            .WithOccurredAt(occurredAt)
            .WithFab("hamburg")
            .WithFabScope(FabScope.Owned)
            .WithEventKind("CameraRegisteredV1")
            .WithResource(ResourceKind.Camera.Value, resourceIdentifier)
            .WithActor(actor, "operator@hamburg.test")
            .WithEventIdentifier(eventIdentifier)
            .WithPayload(payload)
            .Build();
        TestAuditEventQuerySource source = new([row]);
        GetAuditEventQueryHandler handler = new(source);

        var result = await handler.HandleAsync(
            new GetAuditEventQuery(AuditEventIdentifier.From(row.Id.Value), ["hamburg"]),
            default);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.AuditIdentifier.ShouldBe(row.Id.Value);
        dto.OccurredAt.ShouldBe(occurredAt);
        dto.ReceivedAt.ShouldBe(DateTimeOffset.Parse("2026-05-29T08:14:34Z", CultureInfo.InvariantCulture));
        dto.ReceivedAt.ShouldNotBe(dto.OccurredAt);
        dto.Fab.ShouldBe("hamburg");
        dto.EventKind.ShouldBe("CameraRegisteredV1");
        dto.ResourceKind.ShouldBe(ResourceKind.Camera.Value);
        dto.ResourceIdentifier.ShouldBe(resourceIdentifier);
        dto.ActorIdentifier.ShouldBe(actor);
        dto.ActorIsSystem.ShouldBeFalse();
        dto.ActorUsername.ShouldBe("operator@hamburg.test");
        dto.EventIdentifier.ShouldBe(eventIdentifier);
        dto.EventIdentifier.ShouldNotBe(dto.AuditIdentifier);
        dto.Payload.ShouldBe(row.Payload.Content.Value);
        dto.PayloadSizeBytes.ShouldBe(row.Payload.Size.Value);
        dto.PayloadSizeBytes.ShouldBe(Encoding.UTF8.GetByteCount(dto.Payload));
        dto.SchemaVersion.ShouldBe(row.SchemaVersion.Value);
        dto.FabAttribution.ShouldBe("Resolved");
    }

    [Fact]
    public async Task A_system_actor_row_with_no_fab_resource_or_username_maps_the_absences()
    {
        var row = new AuditEventBuilder()
            .WithFab(null)
            .WithFabScope(FabScope.Neutral)
            .WithResource(null, null)
            .WithActor(Guid.Empty, null)
            .Build();
        TestAuditEventQuerySource source = new([row]);
        GetAuditEventQueryHandler handler = new(source);

        var result = await handler.HandleAsync(
            new GetAuditEventQuery(AuditEventIdentifier.From(row.Id.Value), []),
            default);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.Fab.ShouldBeNull();
        dto.ResourceKind.ShouldBeNull();
        dto.ResourceIdentifier.ShouldBeNull();
        dto.ActorUsername.ShouldBeNull();
        dto.ActorIdentifier.ShouldBe(Guid.Empty);
        dto.ActorIsSystem.ShouldBeTrue();
        dto.FabAttribution.ShouldBe("NotApplicable");
    }
}
