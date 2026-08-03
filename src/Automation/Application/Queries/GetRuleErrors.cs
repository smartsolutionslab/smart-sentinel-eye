using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

public abstract record GetRuleError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RuleNotFound(string Name)
        : GetRuleError(
            "RULE_NOT_FOUND",
            $"No rule named '{Name}' exists.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// The name resolves in more than one of the caller's own fabs. Names are
    /// unique per fab rather than globally (spec 013), so a multi-fab operator
    /// asking by name alone has asked an ambiguous question. Naming the
    /// candidates' fabs would leak nothing — they are all fabs the caller
    /// already holds — but the caller still has to say which one they meant.
    /// </summary>
    public sealed record FabAmbiguous(string Name, string Fabs)
        : GetRuleError(
            "RULE_FAB_AMBIGUOUS",
            $"'{Name}' exists in more than one of your fabs ({Fabs}). Name the one you mean with ?fabId=.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="GetRuleError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetRuleFailures
{
    public static GetRuleError RuleNotFound(string name) =>
        new GetRuleError.RuleNotFound(name);

    public static GetRuleError FabAmbiguous(string name, string fabs) =>
        new GetRuleError.FabAmbiguous(name, fabs);
}
