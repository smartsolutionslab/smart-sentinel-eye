using System.Net;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

public sealed record PublishRuleCommand(RuleName Name, int ExpectedVersion)
    : ICommand<Result<RuleIdentifier, PublishRuleError>>;

public abstract record PublishRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RuleNotFound(string Name)
        : PublishRuleError(
            "RULE_NOT_FOUND",
            $"No rule named '{Name}' exists.",
            HttpStatusCode.NotFound);

    public sealed record RuleAlreadyArchived(string Name)
        : PublishRuleError(
            "RULE_ALREADY_ARCHIVED",
            $"Rule '{Name}' is Archived; clone it to author a new one.",
            HttpStatusCode.Conflict);

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
