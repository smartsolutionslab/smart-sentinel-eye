using System.Net;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

public sealed record PublishRuleCommand(FabIdentifier Fab, RuleName Name, int ExpectedVersion)
    : ICommand<Result<RuleIdentifier, PublishRuleError>>;

public abstract record PublishRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RuleNotFound(string Name)
        : PublishRuleError(
            "RULE_NOT_FOUND",
            $"No rule named '{Name}' exists.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// The caller acted on a version of the rule that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record RuleStale(string Name, int ExpectedVersion, int ActualVersion)
        : PublishRuleError(
            "RULE_STALE",
            $"Rule '{Name}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="PublishRuleError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class PublishRuleFailures
{
    public static PublishRuleError RuleNotFound(string name) =>
        new PublishRuleError.RuleNotFound(name);

    public static PublishRuleError RuleStale(string name, int expectedVersion, int actualVersion) =>
        new PublishRuleError.RuleStale(name, expectedVersion, actualVersion);
}
