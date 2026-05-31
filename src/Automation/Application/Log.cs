using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled RuleAction case {Case} on rule {Rule}.")]
    public static partial void UnhandledRuleActionCase(ILogger logger, string @case, RuleIdentifier rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Predicate evaluation failed on rule {Rule}; skipping rule.")]
    public static partial void PredicateEvaluationFailed(ILogger logger, Exception exception, RuleIdentifier rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Value-expression evaluation failed on rule {Rule}; skipping action.")]
    public static partial void ValueExpressionEvaluationFailed(ILogger logger, Exception exception, RuleIdentifier rule);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived rule {Rule} '{Name}'.")]
    public static partial void ArchivedRule(ILogger logger, RuleIdentifier rule, RuleName name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published rule {Rule} '{Name}'.")]
    public static partial void PublishedRule(ILogger logger, RuleIdentifier rule, RuleName name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created rule {Rule} '{Name}' ({TriggerSource}/{TriggerKind}) by {Operator}.")]
    public static partial void CreatedRule(ILogger logger, RuleIdentifier rule, RuleName name, string triggerSource, string triggerKind, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fanned out {Count} action(s) for {EventIdentifier} ({Source}/{Kind}).")]
    public static partial void FannedOutActions(ILogger logger, int count, Guid eventIdentifier, string source, string kind);
}
