using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class CreateLayoutDraftCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task First_creation_with_a_unique_name_returns_a_new_LayoutIdentifier()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            new CreateLayoutDraftCommand(
                LayoutName.From("Line-1"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layouts.Layouts.Count.ShouldBe(1);
        Layout created = layouts.Layouts[0];
        created.Id.ShouldBe(result.Value);
        created.Name.Value.ShouldBe("Line-1");
        created.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
    }

    [Fact]
    public async Task Creating_with_an_overlay_identifier_carries_it_onto_the_initial_Draft()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            new CreateLayoutDraftCommand(
                LayoutName.From("Line-1"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                overlay),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layouts.Layouts[0].Revisions.Single().Overlay.ShouldBe(overlay);
    }

    [Fact]
    public async Task A_name_collision_with_a_non_archived_chain_returns_LayoutNameTaken()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout existing = Layout.CreateDraft(
            LayoutName.From("Line-1"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        layouts.Add(existing);

        CreateLayoutDraftCommandHandler handler = new(layouts, clock, NullLogger<CreateLayoutDraftCommandHandler>.Instance);
        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            new CreateLayoutDraftCommand(
                LayoutName.From("Line-1"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateLayoutDraftError.LayoutNameTaken>();
        layouts.Layouts.Count.ShouldBe(1);
    }
}
