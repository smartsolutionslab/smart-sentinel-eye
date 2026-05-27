using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Mutates a Draft revision's camera (and, per spec 004, its optional
/// overlay binding) in place. The overlay field is a tri-state via
/// <see cref="OverlayChange"/>: <c>None</c> leaves the binding
/// untouched, <c>Set(overlayIdentifier)</c> updates it,
/// <c>Clear()</c> removes it. This avoids confusing "null means
/// clear" with "null means leave alone".
/// </summary>
public sealed record EditDraftRevisionCommand(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    CameraIdentifier Camera,
    OverlayChange Overlay = default)
    : ICommand<Result<LayoutRevisionNumber, EditDraftRevisionError>>;

/// <summary>
/// Three-valued overlay-edit input. ``default(OverlayChange)`` is
/// equivalent to <see cref="None"/> so existing callers that don't
/// pass the new argument keep working unchanged.
/// </summary>
public readonly record struct OverlayChange(bool ShouldChange, OverlayIdentifier? Value)
{
    public static OverlayChange None => default;

    public static OverlayChange Set(OverlayIdentifier overlay) => new(true, overlay);

    public static OverlayChange Clear() => new(true, null);
}
