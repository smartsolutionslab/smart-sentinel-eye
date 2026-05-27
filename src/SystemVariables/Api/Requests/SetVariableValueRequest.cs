namespace SmartSentinelEye.SystemVariables.Api.Requests;

/// <summary>
/// PUT /system-variables/{name}/value body. <c>Value</c> is the
/// wire-string per FR-007; the application layer parses it against the
/// variable's declared type at handle time.
/// </summary>
public sealed record SetVariableValueRequest(string Value);
