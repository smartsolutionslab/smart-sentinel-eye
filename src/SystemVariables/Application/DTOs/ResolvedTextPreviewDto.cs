namespace SmartSentinelEye.SystemVariables.Application.DTOs;

/// <summary>
/// The wire shape of <c>GET /system-variables/resolve</c> (spec 148
/// plan.md "The endpoint"). <see cref="ResolvedText"/> is exactly what the
/// wall would render for this text (FR-011); <see cref="Placeholders"/> is
/// what only the server can tell the operator, since the resolved text alone
/// cannot distinguish an unknown name from an unset or archived one.
/// </summary>
public sealed record ResolvedTextPreviewDto(
    string ResolvedText,
    IReadOnlyList<PlaceholderResolutionDto> Placeholders);

/// <summary>
/// One referenced name's outcome on the wire. <c>Outcome</c> is a string —
/// "Resolved" | "Unknown" | "Unset" | "Archived" — matching
/// <c>VariableDto.State</c>/<c>.Type</c> in the same group. <c>Fab</c> is
/// null unless the name resolved somewhere (Unknown never names a fab, by
/// definition). <c>RenderedValue</c> is null unless <c>Outcome</c> is
/// <c>"Resolved"</c>, and it is <c>VariableValue.Render(...)</c>'s output —
/// <b>not</b> <c>ToWireString()</c>. The two differ for every Number (G17 vs
/// shortest round-trip) and every Boolean (raw bool vs the labels) — this is
/// the single field spec 148's decision 1 turns on.
/// </summary>
public sealed record PlaceholderResolutionDto(
    string Name,
    string Outcome,
    string? Fab,
    string? RenderedValue);
