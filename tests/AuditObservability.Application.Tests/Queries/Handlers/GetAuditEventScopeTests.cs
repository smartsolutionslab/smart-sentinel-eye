using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

// Spec 396 (#2927), extended by spec 422 (#3035): the single-row read answers a hidden
// Unresolved row exactly as it answers an unknown id, whether the caller is fab-scoped or
// holds no usable fab claim.
public class GetAuditEventScopeTests
{
    private static AuditEventEntity UnresolvedRow() =>
        new AuditEventBuilder().WithFab(null).WithFabScope(FabScope.Owned).Build();

    private static AuditEventEntity NotApplicableRow() =>
        new AuditEventBuilder().WithFab(null).WithFabScope(FabScope.Neutral).Build();

    private static async Task<Result<AuditRowDto, GetAuditEventError>> Read(
        AuditEventEntity? stored,
        Guid requested,
        params string[] callerFabs)
    {
        TestAuditEventQuerySource source = new(stored is null ? [] : [stored]);
        GetAuditEventQueryHandler handler = new(source);

        return await handler.HandleAsync(
            new GetAuditEventQuery(AuditEventIdentifier.From(requested), callerFabs),
            default);
    }

    [Fact]
    public async Task An_unresolved_row_is_not_found_for_a_fab_scoped_caller()
    {
        var row = UnresolvedRow();
        row.FabAttribution.ShouldBe(FabAttribution.Unresolved);

        var hidden = await Read(row, row.Id.Value, "munich");
        var unknown = await Read(null, row.Id.Value, "munich");

        hidden.IsFailure.ShouldBeTrue();
        var error = hidden.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>();
        error.Code.ShouldBe("AUDIT_EVENT_NOT_FOUND");
        error.Status.ShouldBe(System.Net.HttpStatusCode.NotFound);
        error.AuditIdentifier.ShouldBe(row.Id.Value);
        hidden.Error.ShouldBe(unknown.Error);
        hidden.Error.Message.ShouldBe(unknown.Error.Message);
    }

    [Fact]
    public async Task A_multi_fab_caller_is_scoped()
    {
        var row = UnresolvedRow();

        var result = await Read(row, row.Id.Value, "berlin", "munich");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>();
    }

    // "Munich" is outside FabIdentifier's grammar (lower-case only), so the
    // caller has no usable claim and holds no fab: it reads only fab-neutral rows.
    [Theory]
    [InlineData(null)]
    [InlineData("Munich")]
    public async Task An_unresolved_row_is_not_found_for_a_caller_with_no_usable_fab_claim(string? claimedFab)
    {
        var row = UnresolvedRow();

        var hidden = claimedFab is null
            ? await Read(row, row.Id.Value)
            : await Read(row, row.Id.Value, claimedFab);
        var unknown = claimedFab is null
            ? await Read(null, row.Id.Value)
            : await Read(null, row.Id.Value, claimedFab);

        hidden.IsFailure.ShouldBeTrue();
        var error = hidden.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>();
        error.Code.ShouldBe("AUDIT_EVENT_NOT_FOUND");
        error.Status.ShouldBe(System.Net.HttpStatusCode.NotFound);
        error.AuditIdentifier.ShouldBe(row.Id.Value);
        hidden.Error.ShouldBe(unknown.Error);
        hidden.Error.Message.ShouldBe(unknown.Error.Message);
    }

    [Fact]
    public async Task A_not_applicable_row_is_returned_to_a_fab_scoped_caller()
    {
        var row = NotApplicableRow();

        var result = await Read(row, row.Id.Value, "munich");

        result.IsSuccess.ShouldBeTrue();
        result.Value.FabAttribution.ShouldBe("NotApplicable");
    }

    [Fact]
    public async Task A_resolved_row_of_another_fab_is_returned_for_the_endpoint_guard_to_refuse()
    {
        var row = new AuditEventBuilder().WithFab("berlin").Build();

        var result = await Read(row, row.Id.Value, "munich");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Fab.ShouldBe("berlin");
    }
}
