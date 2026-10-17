using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

public class GetAuditEventQueryHandlerTests
{
    [Fact]
    public async Task Returns_the_row_with_full_payload_for_a_known_id()
    {
        var row = new AuditEventBuilder().Build();
        TestAuditEventQuerySource source = new([row]);
        GetAuditEventQueryHandler handler = new(source);

        var result = await handler.HandleAsync(new GetAuditEventQuery(AuditEventIdentifier.From(row.Id.Value), []), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AuditIdentifier.ShouldBe(row.Id.Value);
        result.Value.Payload.ShouldBe(row.Payload.Content.Value);
        result.Value.EventKind.ShouldBe(row.EventKind.Value);
    }

    [Fact]
    public async Task Returns_AuditEventNotFound_for_an_unknown_id()
    {
        TestAuditEventQuerySource source = new([]);
        GetAuditEventQueryHandler handler = new(source);
        var missing = Guid.CreateVersion7();

        var result = await handler.HandleAsync(new GetAuditEventQuery(AuditEventIdentifier.From(missing), []), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>();
        ((GetAuditEventError.AuditEventNotFound)result.Error).AuditIdentifier.ShouldBe(missing);
    }

    // Spec 306 (#2540) — the wire value must be exactly the enum name
    // ("NotApplicable"), not a different casing or a serialiser default.
    [Fact]
    public async Task The_returned_row_carries_its_fab_attribution_by_name()
    {
        var row = new AuditEventBuilder()
            .WithFab(null)
            .WithFabScope(FabScope.Neutral)
            .Build();
        TestAuditEventQuerySource source = new([row]);
        GetAuditEventQueryHandler handler = new(source);

        var result = await handler.HandleAsync(
            new GetAuditEventQuery(AuditEventIdentifier.From(row.Id.Value), []), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FabAttribution.ShouldBe("NotApplicable");
    }
}
