using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

public abstract record DryRunRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RuleNotFound(string Name)
        : DryRunRuleError(
            "RULE_NOT_FOUND",
            $"No rule named '{Name}' exists.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// Same ambiguity as the read path: the name resolves in more than one of
    /// the caller's fabs, and a dry run must not silently pick one — trialling
    /// a rule the caller did not mean is worse than refusing.
    /// </summary>
    public sealed record FabAmbiguous(string Name, string Fabs)
        : DryRunRuleError(
            "RULE_FAB_AMBIGUOUS",
            $"'{Name}' exists in more than one of your fabs ({Fabs}). Name the one you mean with ?fabId=.",
            HttpStatusCode.BadRequest);

    public sealed record SampleEventNotJson(string Reason)
        : DryRunRuleError(
            "RULE_DRY_RUN_SAMPLE_INVALID",
            $"The sample event is not valid JSON: {Reason}",
            HttpStatusCode.BadRequest);

    /// <summary>
    /// The rule's own AEL failed to evaluate against this sample — a type
    /// mismatch, say, or a missing field. Surfaced rather than reported as
    /// "did not match", because those are different answers: one means the
    /// rule is fine and the event does not qualify, the other means the
    /// rule is broken for that shape of event, which is exactly what a dry
    /// run exists to reveal.
    /// </summary>
    public sealed record EvaluationFailed(string Reason)
        : DryRunRuleError(
            "RULE_DRY_RUN_EVALUATION_FAILED",
            $"Evaluating the rule against the sample failed: {Reason}",
            HttpStatusCode.BadRequest);
}
