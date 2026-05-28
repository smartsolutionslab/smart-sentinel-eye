using System.Net;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

public sealed record PublishRuleCommand(RuleName Name)
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
}
