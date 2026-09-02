using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

public class GetAuditEventQueryHandlerTests
{
    [Fact]
    public async Task Returns_the_row_with_full_payload_for_a_known_id()
    {
        AuditEventEntity row = new AuditEventBuilder().Build();
        TestAuditEventQuerySource source = new([row]);
        GetAuditEventQueryHandler handler = new(source);

        Result<AuditRowDto, GetAuditEventError> result = await handler.HandleAsync(new GetAuditEventQuery(AuditEventIdentifier.From(row.Id.Value)), default);

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
        Guid missing = Guid.CreateVersion7();

        Result<AuditRowDto, GetAuditEventError> result = await handler.HandleAsync(new GetAuditEventQuery(AuditEventIdentifier.From(missing)), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>();
        ((GetAuditEventError.AuditEventNotFound)result.Error).AuditIdentifier.ShouldBe(missing);
    }
}
