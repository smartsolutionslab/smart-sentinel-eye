using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class EditDraftRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Editing_a_Draft_updates_the_camera()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        CameraIdentifier original = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier replacement = CameraIdentifier.From(Guid.CreateVersion7());
        Layout layout = new LayoutBuilder().ForCamera(original).At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(layout.Id, LayoutRevisionNumber.One, replacement),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().Camera.ShouldBe(replacement);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        EditDraftRevisionCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<EditDraftRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                LayoutIdentifier.New(),
                LayoutRevisionNumber.One,
                CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_LayoutRevisionNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                layout.Id,
                LayoutRevisionNumber.From(42),
                CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutRevisionNotFound>();
    }

    [Fact]
    public async Task Editing_with_OverlayChange_Set_binds_the_overlay()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Layout layout = new LayoutBuilder().ForCamera(camera).At(FixedMoment).Build();
        layouts.Add(layout);
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());

        EditDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                layout.Id, LayoutRevisionNumber.One, camera, OverlayChange.Set(overlay)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().Overlay.ShouldBe(overlay);
    }

    [Fact]
    public async Task Editing_with_OverlayChange_Clear_removes_the_binding()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Layout layout = new LayoutBuilder().ForCamera(camera).WithOverlay(overlay).At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                layout.Id, LayoutRevisionNumber.One, camera, OverlayChange.Clear()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().Overlay.ShouldBeNull();
    }

    [Fact]
    public async Task Editing_with_OverlayChange_None_leaves_the_binding_unchanged()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Layout layout = new LayoutBuilder().ForCamera(camera).WithOverlay(overlay).At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                layout.Id, LayoutRevisionNumber.One, camera),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().Overlay.ShouldBe(overlay);
    }

    [Fact]
    public async Task Editing_a_Published_revision_returns_NotADraft()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                layout.Id,
                LayoutRevisionNumber.One,
                CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.NotADraft>();
    }
}
