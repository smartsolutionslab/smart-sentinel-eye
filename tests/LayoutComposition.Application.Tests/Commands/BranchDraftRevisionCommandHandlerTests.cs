using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class BranchDraftRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Branching_off_a_Published_revision_mints_revision_N_plus_1()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        layouts.Add(layout);

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(layout.Id, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        layout.Revisions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        BranchDraftRevisionCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(LayoutIdentifier.New(), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Chain_without_a_Published_revision_returns_NoPublishedRevisionToBranchFrom()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout draftOnly = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(draftOnly);

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(draftOnly.Id, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.NoPublishedRevisionToBranchFrom>();
    }
}
