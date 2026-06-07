using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Failure cases for the registered-client list queries (issues
/// #826/#827). Carries Code, Message, and HttpStatusCode for RFC 7807
/// mapping (ADR-0089). The v1 list endpoints have no rejectable input of
/// their own (the fab is parsed at the trust boundary and the kind is
/// fixed per endpoint); this base exists so the read side mirrors the
/// <c>Result&lt;T, Error&gt;</c> shape of every other query and can grow
/// failure cases without changing the handler signature.
/// </summary>
public abstract record ListClientsError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status);
