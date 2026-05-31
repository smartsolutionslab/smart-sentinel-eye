using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class RevertRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Reverting_a_Published_revision_transitions_it_to_Draft()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        layouts.Add(layout);

        RevertRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(layout.Id, LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        RevertRevisionCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<RevertRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                LayoutIdentifier.New(), LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RevertRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_LayoutRevisionNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        RevertRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                layout.Id, LayoutRevisionNumber.From(99), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RevertRevisionError.LayoutRevisionNotFound>();
    }

    [Fact]
    public async Task Reverting_a_Draft_returns_NotPublished()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        RevertRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                layout.Id, LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RevertRevisionError.NotPublished>();
    }
}
