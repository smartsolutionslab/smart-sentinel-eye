namespace SmartSentinelEye.Automation.Application.DTOs;

/// <summary>
/// Read-side projection of a <c>Rule</c> (spec 007 T033).
///
/// <para>
/// The predicate goes over the wire as the raw AEL string, not a parsed
/// tree: it is what the author typed, it round-trips into the editor
/// unchanged, and the parse shape is an internal detail that must stay free
/// to change without breaking the API.
/// </para>
///
/// <para>
/// The action is a discriminated wire object — a <see cref="RuleActionDto"/>
/// carrying a <c>Kind</c> tag plus the fields for that variant, with the
/// others null. A flat "one object with every field" shape would let a
/// caller construct a nonsensical combination; the tag makes the variant
/// explicit and keeps the two shapes independent.
/// </para>
/// </summary>
public sealed record RuleDto(
    Guid RuleIdentifier,
    /// <summary>
    /// Optimistic-concurrency version (ADR-0113). Echoed back via
    /// <c>If-Match</c> to mutate; also on the body so the list endpoint hands
    /// every row a version without a per-row fetch.
    /// </summary>
    int Version,
    /// <summary>
    /// The fab this rule belongs to (spec 013). On the body so an operator
    /// holding more than one fab can tell rows apart; a rule never appears
    /// here unless the caller is assigned to its fab.
    /// </summary>
    string Fab,
    string Name,
    string TriggerSource,
    string TriggerKind,
    string Predicate,
    RuleActionDto Action,
    string State,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// Discriminated wire shape for <c>RuleAction</c>. <see cref="Kind"/> is the
/// tag; exactly one variant's fields are populated.
/// </summary>
public sealed record RuleActionDto(
    string Kind,
    string? VariableName,
    string? ValueExpression,
    Guid? Overlay,
    int? DurationMs)
{
    public const string SetVariableValueKind = "SetVariableValue";
    public const string HighlightOverlayKind = "HighlightOverlay";

    public static RuleActionDto ForSetVariableValue(string variableName, string valueExpression) =>
        new(SetVariableValueKind, variableName, valueExpression, null, null);

    public static RuleActionDto ForHighlightOverlay(Guid overlay, int durationMs) =>
        new(HighlightOverlayKind, null, null, overlay, durationMs);
}

/// <summary>
/// Outcome of a dry run (spec 007 T089): whether the predicate matched the
/// supplied sample event, and — when it did and the action is
/// <c>SetVariableValue</c> — the value the action would have written.
/// Nothing is persisted and no integration event is published.
/// </summary>
public sealed record DryRunResultDto(bool Matched, string? EvaluatedValue);
