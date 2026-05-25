# ADR-0089: Augments ADR-0047 — ApiError Value-Record with HTTP Status

**Status:** Accepted (augments ADR-0047)
**Date:** 2026-05-25

## Context

ADR-0047 picked `Result<T, Error>` two-generic with sealed-record
error hierarchies per handler. Yumney's `ApiError` pattern packs the
HTTP status code into the error type itself, removing per-handler
case mapping at the API boundary.

## Decision

Introduce a shared **`ApiError`** abstract record in `Shared.Kernel`:

```csharp
public abstract record ApiError(
    string Code,
    string Message,
    HttpStatusCode Status);
```

Per-handler error unions inherit from `ApiError`:

```csharp
public abstract record RegisterCameraError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record NameAlreadyTaken
        : RegisterCameraError("CAMERA_NAME_TAKEN", "Camera name already in use.", HttpStatusCode.Conflict);

    public sealed record InvalidUrl(string Url)
        : RegisterCameraError("CAMERA_INVALID_URL", $"RTSP URL '{Url}' is not valid.", HttpStatusCode.BadRequest);
}
```

API endpoint mapping reads `Status` from the failure and emits the
appropriate HTTP response:

```csharp
return result.Match(
    onSuccess: id => Results.Created($"/cameras/{id}", id),
    onFailure: err => Results.Problem(
        title: err.Code,
        detail: err.Message,
        statusCode: (int)err.Status));
```

No per-handler case mapping; the error carries everything the
endpoint needs.

## Consequences

- **Positive:** uniform RFC 7807 Problem Details responses across all
  endpoints.
- **Positive:** less boilerplate in API mapping code.
- **Positive:** error catalog is greppable (`git grep ': ApiError'`
  finds every business failure type).
- **Negative:** error types know about HTTP. Acknowledged trade-off;
  the alternative is per-handler case mapping at the endpoint, which
  drifts in practice.

## Alternatives Considered

- **HTTP status assigned at the endpoint per-case (pre-ADR shape)** —
  more boilerplate; drift risk.
- **String code only, no HTTP status** — endpoint still needs a
  mapping table; doesn't solve the duplication.
