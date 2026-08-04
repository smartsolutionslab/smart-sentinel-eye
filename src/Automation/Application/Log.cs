using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingested event {Event} carried no fab; no rule evaluated.")]
    public static partial void SkippedEventWithoutFab(this ILogger logger, Guid @event);

    /// <summary>
    /// Distinct from <see cref="SkippedEventWithoutFab"/>, and it names the
    /// value. A fab that fails Automation's grammar stops every rule for that
    /// fab, and the two causes want different responses: an event with no fab
    /// is a publisher not stamping one, whereas an event whose fab will not
    /// parse is the two contexts disagreeing about the grammar — which is
    /// diagnosable only if the log says what arrived.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ingested event {Event} carried fab '{Fab}', which is not a valid fab identifier; no rule evaluated.")]
    public static partial void SkippedEventWithUnparseableFab(
        this ILogger logger, Exception exception, Guid @event, string fab);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled RuleAction case {Case} on rule {Rule}.")]
    public static partial void UnhandledRuleActionCase(this ILogger logger, string @case, RuleIdentifier rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Predicate evaluation failed on rule {Rule}; skipping rule.")]
    public static partial void PredicateEvaluationFailed(this ILogger logger, Exception exception, RuleIdentifier rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Value-expression evaluation failed on rule {Rule}; skipping action.")]
    public static partial void ValueExpressionEvaluationFailed(this ILogger logger, Exception exception, RuleIdentifier rule);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived rule {Rule} '{Name}'.")]
    public static partial void ArchivedRule(this ILogger logger, RuleIdentifier rule, RuleName name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published rule {Rule} '{Name}'.")]
    public static partial void PublishedRule(this ILogger logger, RuleIdentifier rule, RuleName name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created rule {Rule} '{Name}' ({TriggerSource}/{TriggerKind}) by {Operator}.")]
    public static partial void CreatedRule(this ILogger logger, RuleIdentifier rule, RuleName name, string triggerSource, string triggerKind, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fanned out {Count} action(s) for {EventIdentifier} ({Source}/{Kind}).")]
    public static partial void FannedOutActions(this ILogger logger, int count, Guid eventIdentifier, string source, string kind);
}
