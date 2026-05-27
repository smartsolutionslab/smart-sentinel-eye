using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Queries;

public class GetLayoutQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Existing_layout_is_mapped_into_a_LayoutDto_with_ordered_revisions()
    {
        InMemoryLayoutRepository repository = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier op = OperatorIdentifier.From(Guid.CreateVersion7());
        Layout layout = Layout.CreateDraft(
            LayoutName.From("Line-1"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            op, clock);
        layout.Publish(LayoutRevisionNumber.One, op, clock);
        Revision branched = layout.BranchDraft(op, clock);
        repository.Add(layout);

        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery(layout.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        LayoutDto dto = result.Value;
        dto.LayoutIdentifier.ShouldBe(layout.Id.Value);
        dto.Name.ShouldBe("Line-1");
        dto.Revisions.Count.ShouldBe(2);
        dto.Revisions[0].RevisionNumber.ShouldBe(1);
        dto.Revisions[1].RevisionNumber.ShouldBe(branched.Number.Value);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository repository = new();
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery(LayoutIdentifier.New()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetLayoutError.LayoutNotFound>();
    }
}
