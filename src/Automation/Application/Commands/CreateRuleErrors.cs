using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Commands;

public abstract record CreateRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// The name is taken **within this fab** (spec 013 FR-004). The same name
    /// in another fab is a different rule, not a clash, so the message names
    /// the fab — otherwise an operator who can see only their own fab is told
    /// a name is unavailable with no way to find out why.
    /// </summary>
    public sealed record RuleNameTaken(string Fab, string Name)
        : CreateRuleError(
            "RULE_NAME_TAKEN",
            $"A non-archived rule named '{Name}' already exists in fab '{Fab}'.",
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
