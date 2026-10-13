using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>Parses the wire-shape overlay elements of a request into domain elements.</summary>
internal static class OverlayElementParsing
{
    /// <summary>
    /// Parses one wire-shape element into the domain value object. Called in
    /// a loop inside the caller's single <c>try</c> (spec 150; spec 300
    /// #2349 widened it to a kind switch), so the first bad element in the
    /// set 400s the whole set naming its field — the existing
    /// single-element behaviour, reproduced rather than re-plumbed.
    /// </summary>
    internal static OverlayElement ParseElement(ElementRequest request)
    {
        Ensure.That(request).IsNotNull();

        var position = NormalizedPosition.From(request.NormalizedX, request.NormalizedY);
        var size = NormalizedSize.From(request.NormalizedWidth, request.NormalizedHeight);
        var color = ParseColor(request.Color);
        var kind = ParseKind(request.Kind);

        return kind == ElementKind.Text
            ? ParseTextElement(request, position, size, color)
            : ParseShapeElement(kind, request, position, size, color);
    }

    /// <summary>
    /// <see cref="OverlayColor.From"/>'s own guard names its parameter
    /// <c>value</c> (<c>Ensure.That</c>'s default), which is correct for the
    /// value object's own tests (T005) but wrong at this trust boundary —
    /// FR-003 requires the problem detail to name <c>color</c>. Re-thrown
    /// here rather than changed at the source, so the domain-level
    /// expectation stays exactly what T005 pins.
    /// </summary>
    private static OverlayColor ParseColor(string color)
    {
        try
        {
            return OverlayColor.From(color);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"color must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA. Got '{color}'.",
                nameof(color));
        }
    }

    /// <summary>Same re-naming as <see cref="ParseColor"/>, for <see cref="ElementKind.From"/> (FR-002/FR-003).</summary>
    private static ElementKind ParseKind(string kind)
    {
        try
        {
            return ElementKind.From(kind);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"kind: unknown ElementKind '{kind}'.", nameof(kind));
        }
    }

    /// <summary>
    /// A <c>Box</c>/<c>Ellipse</c> carries neither <c>text</c> nor
    /// <c>fontSizePx</c> (FR-004) — guarded here, naming the offending
    /// field, because <see cref="OverlayElement.Box"/>/<see cref="OverlayElement.Ellipse"/>
    /// take no such arguments to silently drop.
    /// </summary>
    private static OverlayElement ParseShapeElement(
        ElementKind kind,
        ElementRequest request,
        NormalizedPosition position,
        NormalizedSize size,
        OverlayColor color)
    {
        RequireNoTextFields(kind, request.Text, request.FontSizePx);

        return kind == ElementKind.Box
            ? OverlayElement.Box(position, size, color)
            : OverlayElement.Ellipse(position, size, color);
    }

    private static void RequireNoTextFields(ElementKind kind, string? text, int? fontSizePx)
    {
        if (text is not null)
        {
            throw new ArgumentException($"text must be absent for a {kind} element.", nameof(text));
        }
        if (fontSizePx is not null)
        {
            throw new ArgumentException($"fontSizePx must be absent for a {kind} element.", nameof(fontSizePx));
        }
    }

    /// <summary>
    /// <paramref name="request"/>'s <c>Text</c>/<c>FontSizePx</c> are
    /// nullable at the wire boundary (absent for <c>Box</c>/<c>Ellipse</c>).
    /// A missing value here defaults to the type's zero value rather than
    /// being guarded directly, so the factory's own guards
    /// (<see cref="TextContent.From"/>) are the single place that rejects it
    /// — naming <c>text</c> or <c>fontSizePx</c> exactly as they would for a
    /// present-but-invalid value.
    /// </summary>
    private static OverlayElement ParseTextElement(
        ElementRequest request,
        NormalizedPosition position,
        NormalizedSize size,
        OverlayColor color)
    {
        var text = request.Text ?? string.Empty;
        var fontSizePx = request.FontSizePx ?? 0;
        return OverlayElement.TextElement(text, fontSizePx, position, size, color);
    }
}
