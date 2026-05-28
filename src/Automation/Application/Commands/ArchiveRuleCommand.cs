using System.Net;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

public sealed record ArchiveRuleCommand(RuleName Name)
    : ICommand<Result<RuleIdentifier, ArchiveRuleError>>;

public abstract record ArchiveRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RuleNotFound(string Name)
        : ArchiveRuleError(
            "RULE_NOT_FOUND",
            $"No rule named '{Name}' exists.",
            HttpStatusCode.NotFound);
}
