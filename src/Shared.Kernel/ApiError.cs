using System.Net;

namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Base value-record for application errors per ADR-0089. Carries the HTTP
/// status code alongside the code and message so endpoint handlers can emit
/// RFC 7807 Problem Details without per-case mapping.
/// </summary>
public abstract record ApiError(string Code, string Message, HttpStatusCode Status);
