# Spec 281 — The fab the watch path forgot

**Issue**: #2092 · **Branch**: `fix-2092-whep-fab-authorization` · **Phase**: 1 (Specify)
**Date**: 2026-09-28 · **Base**: `0b949ef5` (`origin/develop`)
**Context**: `src/StreamDistribution/Application/Auth/`, `src/StreamDistribution/Infrastructure/Auth/`,
`src/StreamDistribution/Application/Commands/{AuthorizeWhepErrors.cs,Handlers/AuthorizeWhepCommandHandler.cs}`
and their tests. No contract change, no other context.
**Engineer**: delivered directly in this session · **Reviewer**: self-review + security-reviewer pass
(§6) — this touches auth and fab resolution, so `security-reviewer` runs per CLAUDE.md's rule.
**Phase 4a colour**: **behaviour-changing** — new fact observed red first (ADR-0139). This is a genuine
authorization gap being closed, not a refactor.
**ADRs**: **ADR-0161** (new, this spec) decides the mechanism. ADR-0116 (a null-fab stream is visible to
nobody — applied, not re-decided, here). ADR-0114 (fab inference precedent, not reused directly — see
ADR-0161's Alternatives). Constitution §VIII (kiosk view-only scopes — unaffected, this is fab not
scope).
**Constitution**: §IV — N/A. `/streams/authorize` is MediaMTX's WHEP admission hook, not a leg of the
event→overlay path (matches the existing N/A note on the rate-limit spec for the same endpoint).
**New ADR needed**: **Yes — ADR-0161, written as part of this spec** (not deferred), since this closes a
live security gap and the issue itself says a human decision is required before implementation.

---

## 1. The gap (found by #2090's phase-6 security review, pre-existing)

`AuthorizeWhepCommandHandler` checks the `sse.streams.read` scope, then the stream's liveness, then
admits. It never checks fab. `WhepAuthSubject` is `(Subject, Scopes)` — the token's `groups` claim is
discarded before reaching the handler; `StreamRepository.GetByPathAsync` has no fab predicate;
`Stream.Fab` is never read on this path. Full detail: ADR-0161 §Context.

**Impact**: any caller holding `sse.streams.read` (the default scope for `kiosk-web`, `kiosk-wall`,
`smart-sentinel-eye-web`) can open a live WHEP session for a camera in a fab they hold no group in, if
they can obtain its GUID. The read path (`GET /streams/{id}`) is already fab-filtered; the watch path is
not.

## 2. Decision (ADR-0161, this spec)

`WhepAuthSubject` gains `Fabs`, populated from the token's `groups` claim via the existing
`FabClaims.AssignedFabs`. The handler refuses when the stream has a fab the subject's `Fabs` doesn't
include, **or when the stream has no fab yet** (ADR-0116: null-fab is visible to nobody). An
unregistered path (no stream at all) is unaffected — stays admitted, per the existing, separately
accepted "MediaMTX will 404 it anyway" behaviour.

## 3. Scope

**In**:
- `WhepAuthSubject.cs`: add `Fabs`.
- `WhepAuthValidator.cs`: populate it via `FabClaims.AssignedFabs(principal)`.
- `AuthorizeWhepErrors.cs`: new `FabNotAuthorized` error, 403, own message.
- `AuthorizeWhepCommandHandler.cs`: the check, placed after the scope check and before the stream-state
  check (so a caller with no fab access learns nothing about whether the stream happens to be online).
- `AuthorizeWhepCommandHandlerTests.cs`: update all 12 existing `WhepAuthSubject` construction sites with
  an explicit `Fabs` argument; new facts for the fab-refused and null-fab-refused cases.
- A new integration test with a genuinely cross-fab principal (`WhepAuthIntegrationTests.cs` or a new
  file — decided in Design, §5).

**Out** — do not do:
- The unregistered-path admission gap (ADR-0161 Decision — a separate, already-accepted design point,
  not this issue's finding).
- #2540 (null-fab cross-fab readability on the *other* two read paths that issue names) — different
  issue, different paths, not reopened here.
- Anything about `StreamFabAttributionService`, `AttributeToFab`, or the backfill migration — unrelated
  to the authorize hook.
- `IFabAuthorizationGuard` itself — not reused (ADR-0161 Alternatives explains why), not modified.

## 4. User story

### US1 (P1) — A caller cannot watch a camera outside their own fab through WHEP

As a security reviewer, I want the WHEP authorize hook to refuse a session for a stream outside every
fab the caller's token names, so that holding the read scope is not sufficient to watch video from a
plant the caller has no group membership in — closing the gap the read path already closed for the
equivalent list/get endpoints.

**Independent test**: a caller with a real, single-fab (`berlin`) token, against a real stream
provisioned in a different fab (`munich`), is refused 403 — the fact fails today (the current build
admits it) and passes after.

### US2 (P2) — A caller cannot watch a not-yet-attributed stream either

As the same reviewer, I want a stream with no fab recorded yet (a legacy row `StreamFabAttributionService`
has not backfilled) to be refused to everyone, not admitted — matching ADR-0116's own "visible to
nobody" decision applied consistently.

**Independent test**: a stream built with no fab attributed, any caller's subject — refused, regardless
of what fabs the caller holds.

## 5. Design

See ADR-0161 in full for the mechanism and its alternatives. Placement of the check: after
`RequiredScope`, before `StreamState.Offline` — a caller refused on fab learns nothing about the
stream's health, matching the "wrong fab and doesn't exist read the same" precedent
`StreamFabScopingIntegrationTests.Another_fabs_stream_is_indistinguishable_from_a_camera_with_no_stream`
already sets for the read path (that test's exact indistinguishability guarantee is not re-asserted here
— WHEP's contract is status-only, per `AuthorizeWhepErrors.cs`'s own top comment, so there is no response
body for two refusals to differ in the way that test cares about).

**Integration test location**: added to `WhepAuthIntegrationTests.cs` rather than a new file — it already
holds the endpoint's real-HTTP, real-Keycloak-token coverage, and this is one more fact in the same
shape, not a new concern needing its own class.

**Cross-fab principal**: realm seed's `wall-berlin` / `Wall-berlin-1234`, `/fabs/berlin` only, minted
against the `kiosk-wall` client with `openid sse.streams.read` explicitly requested (via
`GetAccessTokenForClientAsync`, not the cached `GetAccessTokenAsync(username, password)` overload, which
always uses `management-web` and would not prove a wall's own token shape). A camera registered in
`munich` (via `camera-catalog`, an operator holding `munich`), its stream awaited via the existing
`WaitForStreamAsync`-shaped poll (`StreamFabScopingIntegrationTests` already establishes this pattern —
mirrored, not duplicated, since it lives in a different test class with a different fixture reset).

## 6. Review

`security-reviewer` runs at phase 6 — this touches auth, fab resolution and a new authorization site, all
three of CLAUDE.md's named triggers. `backend-reviewer` also runs.

## 7. Latency budget impact

N/A — see the header. `/streams/authorize` is not a leg of the event→overlay path; this changes what the
hook refuses, not how long any leg's own work takes.
