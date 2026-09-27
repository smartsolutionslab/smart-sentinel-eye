using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Tests;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

/// <summary>
/// Phase 4a (spec 269 T003e). Every case here is red on arrival: T002's list
/// handler always answers an empty list regardless of what the query source
/// holds, and none of the scenarios below expect an empty list.
/// </summary>
public class ListSourceModesQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static SourceMode BuildDeclared(Source source, string fab = "dresden") =>
        SourceMode.Declare(
            FabIdentifier.From(fab), source, EventTypeMode.Strict,
            OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));

    [Fact]
    public async Task Listing_returns_only_the_callers_fabs_ordered_by_fab_then_source()
    {
        SourceMode[] seed =
        [
            BuildDeclared(Source.Webhook, "dresden"),
            BuildDeclared(Source.Manual, "dresden"),
            BuildDeclared(Source.Plc, "munich"),
        ];
        ListSourceModesQueryHandler handler = new(new TestSourceModeQuerySource(seed));

        Result<IReadOnlyList<SourceModeDto>, ListSourceModesError> result =
            await handler.HandleAsync(new ListSourceModesQuery([Dresden]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(dto => (dto.Fab, dto.Source)).ShouldBe(
            [("dresden", "manual"), ("dresden", "webhook")]);
    }

    [Fact]
    public async Task Listing_excludes_modes_of_fabs_the_caller_does_not_hold()
    {
        SourceMode[] seed = [BuildDeclared(Source.Manual, "dresden"), BuildDeclared(Source.Manual, "munich")];
        ListSourceModesQueryHandler handler = new(new TestSourceModeQuerySource(seed));

        Result<IReadOnlyList<SourceModeDto>, ListSourceModesError> result =
            await handler.HandleAsync(new ListSourceModesQuery([Dresden]), CancellationToken.None);

        result.Value.ShouldHaveSingleItem().Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task Listing_carries_the_version_of_each_entry()
    {
        SourceMode sourceMode = BuildDeclared(Source.Manual);
        AggregateVersions.SetTo(sourceMode, 5);
        ListSourceModesQueryHandler handler = new(new TestSourceModeQuerySource([sourceMode]));

        Result<IReadOnlyList<SourceModeDto>, ListSourceModesError> result =
            await handler.HandleAsync(new ListSourceModesQuery([Dresden]), CancellationToken.None);

        result.Value.ShouldHaveSingleItem().Version.ShouldBe(5);
    }
}
