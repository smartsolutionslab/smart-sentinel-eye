using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

public class GetAuditEventQueryHandlerTests
{
    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Returns_the_row_with_full_payload_for_a_known_id()
    {
        AuditEventEntity row = new AuditEventBuilder().Build();
        TestAuditEventQuerySource source = new([row]);
        GetAuditEventQueryHandler handler = new(source);

        var result = await handler.HandleAsync(new GetAuditEventQuery(row.Id.Value), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AuditIdentifier.ShouldBe(row.Id.Value);
        result.Value.Payload.ShouldBe(row.Payload);
        result.Value.EventKind.ShouldBe(row.EventKind.Value);
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Returns_AuditEventNotFound_for_an_unknown_id()
    {
        TestAuditEventQuerySource source = new([]);
        GetAuditEventQueryHandler handler = new(source);
        Guid missing = Guid.CreateVersion7();

        var result = await handler.HandleAsync(new GetAuditEventQuery(missing), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>();
        ((GetAuditEventError.AuditEventNotFound)result.Error).AuditIdentifier.ShouldBe(missing);
    }
}
