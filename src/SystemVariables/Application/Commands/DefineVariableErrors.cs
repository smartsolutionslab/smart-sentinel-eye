using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

public abstract record DefineVariableError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record VariableNameTaken(string Name)
        : DefineVariableError(
            "VARIABLE_NAME_TAKEN",
            $"A non-archived system variable with the name '{Name}' already exists.",
            HttpStatusCode.Conflict);

    public sealed record BooleanLabelsRequired()
        : DefineVariableError(
            "VARIABLE_BOOLEAN_LABELS_REQUIRED",
            "BooleanLabels must be supplied for Boolean variables.",
            HttpStatusCode.BadRequest);

    public sealed record BooleanLabelsOnlyOnBoolean()
        : DefineVariableError(
            "VARIABLE_BOOLEAN_LABELS_ONLY_ON_BOOLEAN",
            "BooleanLabels can only be set on Boolean variables.",
            HttpStatusCode.BadRequest);

    public sealed record InitialValueTypeMismatch(string ExpectedType)
        : DefineVariableError(
            "VARIABLE_TYPE_MISMATCH",
            $"Initial value does not match declared type '{ExpectedType}'.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="DefineVariableError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class DefineVariableFailures
{
    public static DefineVariableError VariableNameTaken(string name) =>
        new DefineVariableError.VariableNameTaken(name);

    public static DefineVariableError BooleanLabelsRequired() =>
        new DefineVariableError.BooleanLabelsRequired();

    public static DefineVariableError BooleanLabelsOnlyOnBoolean() =>
        new DefineVariableError.BooleanLabelsOnlyOnBoolean();

    public static DefineVariableError InitialValueTypeMismatch(string expectedType) =>
        new DefineVariableError.InitialValueTypeMismatch(expectedType);
}
