# ADR-0116: StreamDistribution Attributes Pre-Existing Streams via a Cross-Fab Read-Only Service Account

**Status:** **Accepted**
**Date:** 2026-08-16
**Supersedes:** —
**Superseded by:** —

## Context

Spec 016 gives a stream the fab of the camera it serves. New streams derive it
from `CameraRegisteredV1`, which has carried the camera's fab since spec 015.
Streams provisioned **before** the change have none, and FR-008 says they must
acquire their own camera's fab rather than be guessed into one.

The obvious mechanism — a SQL backfill in the migration, as specs 013, 014 and
015 all used — is unavailable here. `AppHost` registers `camera-catalog-db` and
`stream-distribution-db` as **distinct databases**, so no migration in
StreamDistribution can read the cameras table (`research.md` §5). The
derivation has to happen at runtime, from the context that owns the camera.

Three facts, each established by reading the code rather than assumed, shape
what that runtime call can be:

1. **StreamDistribution has never called another context over HTTP.** This is
   the first such call, and `plan.md` §III records it as a bounded exception to
   constitution §III rather than a clean sheet.
2. **CameraCatalog has no read-by-identifier route.** It exposes exactly
   `POST /cameras` and `GET /cameras`; the missing routes are tracked as #1435.
   So a stream's camera cannot be resolved individually — the catalogue is read
   whole and indexed by identifier.
3. **`GET /cameras` is itself fab-scoped** (spec 015 FR-005) and requires a
   token. It returns only cameras in fabs the caller holds.

Fact 3 is the awkward one. **A stream's fab is precisely what is unknown**, so
the read cannot be narrowed to the right fab in advance — narrowing it would
require the answer. A caller holding one fab resolves only that fab's streams
and leaves every other plant's permanently unattributed, which is FR-008
failing quietly in exactly the deployment the feature exists for.

Deleting the unattributable rows — the precedent set by
`20260728210420_PersistStreamSourceUrl` — does not transfer. Those rows were
already broken; these are functional, video is flowing through them, and
nothing republishes `CameraRegisteredV1`, so deleting would trade a metadata
gap for an outage (`research.md` §3).

## Decision

**StreamDistribution mints a `client_credentials` token for a dedicated
Keycloak service account that is a member of every fab group and holds
`sse.cameras.read` and nothing else, and uses it once at startup to read the
camera catalogue.**

Concretely:

1. A confidential client `stream-distribution-attribution` with
   `serviceAccountsEnabled`. Its service-account user is in `/fabs/munich` and
   `/fabs/dresden` — every fab the realm defines.
2. Its only scope is `sse.cameras.read`. It cannot register a camera, cannot
   touch any other context, and cannot write anything anywhere.
3. `StreamFabAttributionService` is an `IHostedService` **separate from**
   `MediaMtxReconciler` (`research.md` §1). It selects streams where
   `fab IS NULL`, and if there are none it makes no HTTP call and logs nothing
   — the steady state is silent and free.
4. A stream whose camera the catalogue does not return keeps its null fab and
   is counted as unresolved (FR-010). Nothing is ever defaulted.
5. Failure is swallowed deliberately: an unreachable CameraCatalog, or a
   refused token, leaves streams unattributed and lets the host start. Those
   streams are then visible to nobody, which is FR-009 working rather than a
   second failure — and video keeps flowing throughout. Asserted by a test, so
   it is chosen rather than inherited from a `try/catch` that happened to be
   nearby.

## Consequences

### What this buys

Pre-existing streams acquire the fab of their **own** camera, in every fab,
without a guess. The alternative available without this account — attributing
everything to the one fab that happened to be live — is wrong precisely for a
multi-fab deployment.

The blast radius is small and bounded: read-only, one context, one route, at
startup only, and only while unattributed streams exist. After the first
successful pass the call never happens again.

### What it costs, stated plainly

**A principal exists that can read every plant's camera list.** That is a real
widening. Three things bound it rather than excuse it:

- It is **read-only** and scoped to cameras. It cannot see streams, rules,
  variables, layouts or events, and cannot write.
- It is **not on any request path**. Nothing an operator does causes it to be
  used; a compromise of it does not compromise a running request.
- Its credential is a confidential client secret held by one service, injected
  by Aspire as `StreamFabAttribution__ClientSecret` and never checked in
  outside the dev-only realm seed.

**A concurrent health transition can lose the pass.** `StreamHealthWatcher`
polls the same rows every two seconds and writes state changes to them, and
`Stream` carries an EF concurrency token. A transition landing between the
attribution read and its save fails the whole batch. Consistent with ADR-0113
there is no retry-on-conflict: the exception is caught, logged, and the next
host start runs the pass again over rows that are still null. It fails closed —
nothing is half-attributed and nothing is misattributed — but a deployment that
never restarts again would keep those streams invisible, which the unresolved
count in the log is there to surface.

**Its group membership must be maintained.** A fab added to the realm without
adding it to this account's groups leaves that fab's pre-existing streams
unattributed — invisible rather than misattributed, so it fails closed, but it
fails silently in the sense that only the unresolved count in the log says so.
That count is logged on every pass for this reason.

### The narrower alternatives, and why not

- **A per-fab service account.** Would remove the cross-fab principal, but the
  attribution pass would have to try every fab's account against every
  unattributed stream — the same total read access, spread over more
  credentials.
- **Build #1435's `GET /cameras/{identifier}` first.** A cleaner end state and
  worth doing, but it does not remove this decision: a read-by-identifier is
  fab-scoped too, so resolving a stream whose fab is unknown still needs a
  principal that spans fabs.
- **An unauthenticated internal route on CameraCatalog.** Rejected. It trades a
  scoped, auditable credential for an endpoint with no caller identity at all.

### Not in scope

The follow-up migration tightening `streams.fab` to `NOT NULL`. It cannot be
written safely until attribution has demonstrably run in every deployment,
which a migration cannot assert about itself.
