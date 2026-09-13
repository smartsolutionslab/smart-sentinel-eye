using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Tests;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

/// <summary>
/// Phase 4a (spec 143 T003d). Every case here is red on arrival: T002's
/// list handler always answers an empty list regardless of what the query
/// source holds, and none of the scenarios below expect an empty list.
/// </summary>
public class ListEventTypesQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static RegisteredEventType BuildRegistered(string kind, string fab = "dresden") =>
        RegisteredEventType.Register(
            FabIdentifier.From(fab), Kind.From(kind), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));

    [Fact]
    public async Task Listing_returns_the_registered_types_of_the_callers_fabs()
    {
        RegisteredEventType[] seed = [BuildRegistered("AlphaKind"), BuildRegistered("BetaKind")];
        ListEventTypesQueryHandler handler = new(new TestRegisteredEventTypeQuerySource(seed));

        Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError> result =
            await handler.HandleAsync(new ListEventTypesQuery([Dresden]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(dto => dto.Kind).ShouldBe(["AlphaKind", "BetaKind"]);
    }

    [Fact]
    public async Task Listing_excludes_retired_types()
    {
        RegisteredEventType retired = BuildRegistered("RetiredKind");
        retired.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));
        RegisteredEventType[] seed = [BuildRegistered("LiveKind"), retired];
        ListEventTypesQueryHandler handler = new(new TestRegisteredEventTypeQuerySource(seed));

        Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError> result =
            await handler.HandleAsync(new ListEventTypesQuery([Dresden]), CancellationToken.None);

        result.Value.Select(dto => dto.Kind).ShouldBe(["LiveKind"]);
    }

    [Fact]
    public async Task Listing_excludes_types_of_fabs_the_caller_does_not_hold()
    {
        RegisteredEventType[] seed =
        [
            BuildRegistered("DresdenKind", "dresden"),
            BuildRegistered("MunichKind", "munich"),
        ];
        ListEventTypesQueryHandler handler = new(new TestRegisteredEventTypeQuerySource(seed));

        Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError> result =
            await handler.HandleAsync(new ListEventTypesQuery([Dresden]), CancellationToken.None);

        result.Value.Select(dto => dto.Kind).ShouldBe(["DresdenKind"]);
        result.Value.ShouldHaveSingleItem().Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task Listing_orders_by_kind()
    {
        RegisteredEventType[] seed = [BuildRegistered("ZebraKind"), BuildRegistered("AlphaKind")];
        ListEventTypesQueryHandler handler = new(new TestRegisteredEventTypeQuerySource(seed));

        Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError> result =
            await handler.HandleAsync(new ListEventTypesQuery([Dresden]), CancellationToken.None);

        result.Value.Select(dto => dto.Kind).ShouldBe(["AlphaKind", "ZebraKind"]);
    }

    [Fact]
    public async Task Listing_carries_the_version_of_each_entry()
    {
        // Version 5, not 0 — at 0 this assertion would hold even if the
        // projection emitted a constant, since default(int) is also 0
        // (mirrors ListWebhookIntegrationsQueryHandlerTests).
        RegisteredEventType eventType = BuildRegistered("VersionedKind");
        AggregateVersions.SetTo(eventType, 5);
        ListEventTypesQueryHandler handler = new(new TestRegisteredEventTypeQuerySource([eventType]));

        Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError> result =
            await handler.HandleAsync(new ListEventTypesQuery([Dresden]), CancellationToken.None);

        result.Value.ShouldHaveSingleItem().Version.ShouldBe(5);
    }
}
