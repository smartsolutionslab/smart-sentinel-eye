# ADR-0161: The WHEP Authorize Hook Checks Fab By Reading `groups` Off the Hand-Validated Token

**Status:** **Accepted**
**Date:** 2026-09-28
**Supersedes:** —
**Superseded by:** —

## Context

`AuthorizeWhepCommandHandler` (`src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs`)
checks scope, then stream liveness, then admits. **It never checks fab.**
Found by the phase-6 security review of #2090 (issue #2092), pre-existing and
unrelated to that PR's own diff.

The consequence: any caller holding `sse.streams.read` — the default client
scope on `kiosk-web` and `kiosk-wall` — can open a
live WHEP session for **any camera in the system**, not only the ones in
their own fab, provided they can obtain the camera's GUID from somewhere (a
shared layout export, a screenshot, a reassigned operator, logs). The
equivalent read path, `GET /streams/{id}`, is already fab-filtered (spec 016
FR-006) and answers 404 for a stream outside the caller's fabs,
indistinguishable from one that does not exist. **The read path is
fab-filtered; the watch path is not.**

This needs a new mechanism, not a reuse of the existing one, because the WHEP
caller is not the request principal in the usual sense:

- `IFabAuthorizationGuard.EnsureAccessAsync` takes a `ClaimsPrincipal` from
  the current HTTP request's authenticated user. The WHEP hook's principal is
  the **bearer token MediaMTX forwards in the POST body**, hand-validated by
  `WhepAuthValidator` against the realm — there is no ASP.NET Core
  authentication middleware on this path (`/streams/authorize` is
  `AllowAnonymous` at the routing layer; the handler does its own token
  validation).
- `WhepAuthSubject`, the record that carries what the validator extracted,
  is `(Subject, Scopes)` — the token's `groups` claim (Keycloak's fab
  membership claim, the same one `FabClaims`/`IFabAuthorizationGuard` read)
  is discarded before it reaches the handler.
- `Stream.Fab` (`Stream.cs:44`) is `FabIdentifier?` — nullable, because
  streams provisioned before spec 016 have none until
  `StreamFabAttributionService` backfills them at startup. ADR-0116 already
  decided what a null fab means: **"visible to nobody"** (FR-009) — a stream
  with no fab attributed yet is not a stream any caller can watch, not an
  open one.

## Decision

**`WhepAuthSubject` gains a third field, `Fabs`, populated from the token's
`groups` claim via the existing `FabClaims.AssignedFabs(ClaimsPrincipal)`
helper — the same parser `IFabAuthorizationGuard` and every other fab-checked
endpoint already use, not a new hand-rolled one.**

`WhepAuthValidator.ValidateAsync` already builds a `ClaimsPrincipal` from the
hand-validated token (`handler.ValidateToken(...)`) to read `sub`, `azp` and
`scope`; reading `groups` off the same principal costs nothing new and
guarantees the exact same parsing rules as the rest of the product.

`AuthorizeWhepCommandHandler.HandleAsync` gains one check, **after the scope
check and before the stream-state check**, so a caller who cannot see a
stream at all learns nothing about whether it happens to be up:

```csharp
if (stream.HasValue &&
    (stream.Value.Fab is null || !subject.Fabs.Contains(stream.Value.Fab.Value, StringComparer.Ordinal)))
{
    return Failure(AuthorizeWhepFailures.FabNotAuthorized());
}
```

`stream.Value.Fab is null` is included deliberately, not left to fall through
to admission: ADR-0116 already decided a null-fab stream is visible to
nobody, and today's hook — checking nothing about fab — has been treating it
as visible to everybody who holds the scope. This is that decision applied
to a path that never applied it, not a new one.

**An unregistered path is unaffected and stays admitted.** `!stream.HasValue`
(no camera ever registered at this path at all) continues to fall through to
success, exactly as `WhepAuthIntegrationTests` already documents and asserts
— "the WHEP path will 404 later anyway" once MediaMTX itself tries to pull a
source that does not exist. That reasoning holds for a genuinely *fabricated*
path. **It does not fully hold for an *orphaned* one** — security review found
that `ProvisionStreamCommandHandler` calls `MediaMtxRtspGateway.AddPathAsync`
before `streams.SaveAsync`, so a DB failure between the two strands a real,
live MediaMTX path with no `streams` row behind it until the next
`MediaMtxReconciler` pass (which runs once, at startup). Any caller holding
the scope, in any fab, who has that camera's GUID reaches live video through
exactly this fall-through for as long as the orphan survives. This is the
same class of gap #2092 found, on a narrower and pre-existing trigger this
ADR did not originally price in. **Filed separately** (closing it here would
mean either refusing every unregistered path — a behaviour change to
already-accepted, tested admission this ADR did not set out to make — or
reordering provisioning to save-before-register, either of which is its own
decision, not an extension of this one). Widening this ADR to close it would
still be scope creep past what #2092 found; the gap is recorded here instead
of left implicit.

**New error, not a reuse of `Forbidden`.** `AuthorizeWhepError.Forbidden`'s
message is "Bearer token does not grant the sse.streams.read scope" —
reusing it for a fab refusal would misdescribe the reason in server-side
logs and the response body, even though MediaMTX itself only reads the HTTP
status (both are 403; this hook's contract is status-only, per the error
file's own top comment). `WHEP_FAB_NOT_AUTHORIZED` is added, same status,
accurate message — matching the existing one-error-per-reason convention
this file already follows for every other refusal.

## Consequences

**Closes the gap.** A caller can no longer open a WHEP session for a camera
outside every fab their token names, regardless of scope. A stream not yet
fab-attributed is refused rather than silently admitted, matching FR-009.

**One more claim read per WHEP open.** `groups` is present on every token a
client carrying `sse-groups` mints (the same mapper `IFabAuthorizationGuard`
depends on) — no realm change, no new scope, no new client configuration.
**Qualified**: dynamically enrolled device/kiosk clients only started
receiving `sse-groups` on 2026-09-26 (#2619), with no backfill for clients
enrolled earlier. Their tokens carry no `groups` and now lose WHEP too —
failing closed, consistent with (not a new outage beyond) their existing
refusal on every other fab-scoped read.

**A sharper existence oracle than before, disclosed rather than closed.**
Before this change, `/streams/authorize` (anonymous at routing, reachable by
anything with network access to `stream-distribution`, not only MediaMTX)
told any `sse.streams.read` holder only online-vs-not for a *registered*
stream. After this change, the same caller also learns whether a candidate
`cam-{guid}` is registered *at all* — a 403 (fab or health) implies a row
exists; a 200 for an unrecognised caller implies none does, or the caller's
own fab matches. `FabNotAuthorized`'s status alone (403, no fab named, no
health named) does not distinguish "wrong fab" from "right fab but offline"
from a foreign caller's perspective, and the endpoint is rate-limited per
source (spec 208) against a Guid v7 keyspace no realistic caller enumerates.
Accepted as a narrow, disclosed cost of closing the larger gap, not
re-litigated here.

**A multi-fab caller is unaffected.** `Fabs.Contains` checks membership, not
equality, so an operator or wall assigned to more than one fab keeps
watching every stream in every fab they hold — the same shape
`IFabAuthorizationGuard` already gives every other fab-scoped endpoint.

**Does not touch the unregistered-path admission gap** (see Decision) or
resolve #2540 (null-fab cross-fab *readability* on the two other paths that
issue names) — both are pre-existing, separately tracked, and out of this
ADR's scope. This ADR only closes the one gap #2092 found: the watch path
never checked fab at all.

## Alternatives Considered

### Route the WHEP hook through `IFabAuthorizationGuard` directly

Rejected: that interface takes a `ClaimsPrincipal` sourced from the current
authenticated HTTP request. `/streams/authorize` has no such principal — its
caller is MediaMTX, authenticated by nothing; the bearer token it forwards is
validated by hand, inside the application layer, against a `ClaimsPrincipal`
that is a local variable inside `WhepAuthValidator`, never wired into ASP.NET
Core's authentication pipeline. Reusing the guard's *signature* would require
either promoting that hand-validated principal into the request pipeline (a
much larger change touching how MediaMTX's callback is authenticated at all)
or duplicating the guard's throw-based control flow inside a
`Result`-returning handler that the rest of this file deliberately does not
use exceptions for. Reusing `FabClaims.AssignedFabs` — the enumeration half,
already factored apart from the guard for exactly this kind of second
consumer — gets the same parsing correctness without either cost.

### Compare against a single fab (`stream.Fab == callersOneFab`) instead of a list

Rejected. `WhepAuthSubject` would need to pick one fab out of a
possibly-multi-fab caller, and there is no principled way to pick — an
operator assigned to munich and dresden has no "primary" fab recorded
anywhere the token exposes. Carrying the whole list and checking membership
(exactly `IFabAuthorizationGuard`'s own approach) has no such problem and
costs nothing extra to compute.

### Treat a null `Stream.Fab` as admitted (preserve today's behaviour for that one case)

Rejected. ADR-0116 already decided what a null fab means, in this exact
context — "visible to nobody" — and preserving admission here would be this
ADR knowingly contradicting a standing decision rather than applying it. The
narrower alternative (refuse fab-mismatch, admit null-fab) was considered and
rejected specifically because it would leave the hook doing *half* of
FR-009's job on the one path that has never done any of it.

## Implementation Notes

- `WhepAuthSubject` becomes `(string Subject, IReadOnlyList<string> Scopes,
  IReadOnlyList<string> Fabs)`. Every existing construction site (12 in
  `AuthorizeWhepCommandHandlerTests.cs`, one in `WhepAuthValidator.cs`) is
  updated in the same PR — none can be defaulted away, since an implicit
  default for a security-relevant field is exactly the kind of drive-by
  the constitution's guard-writing guidance warns against.
- `FabClaims` lives in `ServiceDefaults.Authorization`, which
  `StreamDistribution.Infrastructure` already references (no new project
  reference).
- Test coverage needs a **genuinely cross-fab** principal — the realm seed's
  `wall-berlin` (`/fabs/berlin` only) against a stream provisioned in munich
  is the integration-level case. Minted via the cached admin-token helper's
  client (`management-web`, `directAccessGrantsEnabled: true`), **not**
  `kiosk-wall` as first planned — `kiosk-wall` is PKCE-only by design (spec
  052), so a password grant against it is refused by Keycloak itself
  (`unauthorized_client`) before ever reaching the WHEP hook, caught by
  running the live fact rather than assumed. `wall-berlin`'s `groups` claim —
  the thing under test — is identical regardless of which client mints the
  token. `WhepAuthIntegrationTests` and `AuthorizeWhepCommandHandlerTests`
  previously had **only positive fab cases** (every existing subject either
  carried no fab claim at all or the stream had none), so this is new
  coverage, not a gap in existing coverage that widening would have caught.
