using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

public abstract record CreateRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RuleNameTaken(string Name)
        : CreateRuleError(
            "RULE_NAME_TAKEN",
            $"A non-archived rule named '{Name}' already exists.",
            HttpStatusCode.Conflict);

    public sealed record PredicateParseFailed(string Reason, int Position)
        : CreateRuleError(
            "RULE_PREDICATE_PARSE_FAILED",
            $"Predicate parse failed at position {Position}: {Reason}",
            HttpStatusCode.BadRequest);

    public sealed record ActionExpressionParseFailed(string Reason, int Position)
        : CreateRuleError(
            "RULE_ACTION_EXPRESSION_PARSE_FAILED",
            $"Action value expression parse failed at position {Position}: {Reason}",
            HttpStatusCode.BadRequest);
}
