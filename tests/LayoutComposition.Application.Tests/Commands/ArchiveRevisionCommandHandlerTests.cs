using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class ArchiveRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Archiving_a_Published_revision_transitions_it_to_Archived()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = Layout.CreateDraft(
            LayoutName.From("Line-1"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        layouts.Add(layout);

        ArchiveRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(layout.Id, LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Archived);
    }

    [Fact]
    public async Task Archiving_a_Draft_is_allowed_and_transitions_to_Archived()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = Layout.CreateDraft(
            LayoutName.From("Line-1"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        layouts.Add(layout);

        ArchiveRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        await handler.HandleAsync(
            new ArchiveRevisionCommand(layout.Id, LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Archived);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        ArchiveRevisionCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<ArchiveRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(
                LayoutIdentifier.New(), LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ArchiveRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_LayoutRevisionNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = Layout.CreateDraft(
            LayoutName.From("Line-1"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        layouts.Add(layout);

        ArchiveRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(
                layout.Id, LayoutRevisionNumber.From(99), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ArchiveRevisionError.LayoutRevisionNotFound>();
    }
}
