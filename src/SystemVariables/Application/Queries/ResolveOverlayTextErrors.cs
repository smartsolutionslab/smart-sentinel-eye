using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// The <see cref="Result{T,E}"/> error type for
/// <see cref="ResolveOverlayTextQuery"/> — kept as the abstract base rather
/// than removed outright (ADR-0047's invariance rule needs a type here even
/// with no variant defined). No case exists: text validation (empty,
/// over-length) is a boundary concern the endpoint answers directly with
/// <c>Results.Problem(...)</c>, mirroring <c>GetSnapshot</c>, and there is no
/// "not found" here either — see plan.md "The endpoint": resolving text that
/// references nothing is a 200 with an empty <c>placeholders</c> list, since
/// there is no resource that could be absent.
/// </summary>
public abstract record ResolveOverlayTextError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status);
