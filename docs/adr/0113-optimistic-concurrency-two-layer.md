# ADR-0113: Two-Layer Optimistic Concurrency — amends ADR-0043

**Status:** **Accepted** (amends ADR-0043)
**Date:** 2026-08-01
**Supersedes:** ADR-0043 in part (three specific points, below)
**Superseded by:** —

## Context

ADR-0043 specified optimistic concurrency with an explicit `Version`
field on every aggregate root. An audit of `develop` on 2026-08-01
(issue #1154) found that **none of it works**, and that the ADR itself
is wrong in three places.

### The mechanism is inert

`AggregateRoot.Version` (`src/Shared.Kernel/AggregateRoot.cs`) is
**never assigned anywhere in `src/`**. No increment, no `SaveChanges`
override, no `UseXminAsConcurrencyToken`, no
`ValueGeneratedOnAddOrUpdate`. It is `0` for every aggregate, for its
whole lifetime.

All ten aggregates nonetheless map it `.IsConcurrencyToken()`. EF
therefore emits:

```sql
UPDATE layouts SET … WHERE layout_id = @id AND version = 0
```

against a row whose version is permanently `0`. Two concurrent writers
both read `0`, both match the predicate, and **both succeed** — last
write silently wins. `DbUpdateConcurrencyException` appears nowhere in
`src/` or `tests/` because nothing can throw it.

Issue #1154 originally described this as "detection works, handling is
missing". That framing is wrong and the distinction matters: adding the
missing `catch` would land a block that **can never execute** — passing
a mocked test, closing the issue, and protecting nothing.

### An EF token would not solve the stated problem anyway

ADR-0043's motivating scenario is "multiple operators… against the same
aggregate". But an EF concurrency token only detects conflicts between
two **overlapping database transactions** — requests microseconds apart.

The scenario operators actually hit is longer-lived:

1. Operator A opens the layout editor.
2. Operator B opens the same layout.
3. A saves.
4. B saves a minute later.

B's request loads fresh state inside its own short transaction and
overwrites A's work cleanly. **No token, however correct, ever fires.**
Detecting this requires the version the client was shown to travel back
with the mutating request — plumbing that does not exist: `Version` is
in no DTO, and there is no `If-Match`, `ETag`, or `expectedVersion`
anywhere in `src/` or `apps/`.

### Three defects in ADR-0043

1. **The Marten exemption is not real.** ADR-0043 states that Overlays
   and Automation are event-sourced via Marten and inherit the guarantee
   from stream versions. **Both are EF Core** with the same inert token.
   Nothing is exempt.
2. **"Retry once" is harmful.** ADR-0043 instructs handlers to retry
   once on conflict before surfacing a failure. Re-applying a mutation
   to freshly-loaded state is *precisely* the silent overwrite this work
   exists to prevent.
3. **It is silent on the cross-request case** — the only one operators
   experience.

### A structural gap in the token itself

For aggregates whose children live in their own tables, changing only a
child means EF updates the child table and **never touches the root
row**, so the root's token is not in any `WHERE` clause even once it
increments correctly. Two aggregates are affected, and they are the two
shared editing surfaces:

| Aggregate | Owned collection | Table |
|---|---|---|
| `Layout` | `Revisions` → `Tiles` | `layout_revisions`, `layout_revision_tiles` |
| `Overlay` | `Revisions` | `overlay_revisions` |

(`Variable.BooleanLabels` is `OwnsOne` flattened onto `variables`, so it
updates the root row and is unaffected.)

## Decision

**Two layers, because they catch different failures.** Neither alone is
sufficient.

### Layer 1 — cross-request expected version (the operator-facing case)

- Read DTOs for operator-mutable aggregates expose `Version`; GET
  responses carry it as `ETag`.
- Mutating endpoints require the expected version in an **`If-Match`**
  header.
- Handlers compare it to the loaded aggregate **after load and before
  mutating**, returning a typed `*Stale` failure on mismatch.
- A mutating request that **omits `If-Match` is rejected with `428
  Precondition Required`** (RFC 6585 — "the origin server requires the
  request to be conditional"). We control both SPAs, so no external
  client breaks, and a silent fallback would reopen exactly the hole
  this ADR closes.

**Transport is `If-Match`, not a request-body field.** 14 of the 28
mutating endpoints take **no request body** — publish, archive, branch
and revert across Layout and Overlay, three DELETEs, and two Automation
POSTs. A body field would mean inventing request bodies for half the
mutating surface; a header covers all 28 uniformly and is the
standards-defined mechanism for conditional requests.

**A stale version returns `409 Conflict`, not `412 Precondition
Failed`.** Strict RFC 7232 would say 412. We choose 409 because the
condition is a domain conflict the caller can act on, it matches the
name spec-003 already gave it (`LayoutRevisionStale`, issues #240/#283),
and it is consistent with the existing convention — `InvalidStateTransition`
is already 409. Note the asymmetry is deliberate: **428 for a missing
precondition, 409 for a failed one.**

### Layer 2 — in-transaction token (the true race)

- A single `ISaveChangesInterceptor` increments `Version` on write.
- It bumps a root that is `Modified`, **and** a root whose own columns
  are untouched but which has a dirty owned descendant — closing the
  structural gap above.
- The bump sets `CurrentValue = OriginalValue + 1`. EF puts
  **OriginalValue** in the `WHERE` clause and **CurrentValue** in the
  `SET`, so this yields the correct predicate; and setting a property on
  an `Unchanged` entry promotes it to `Modified`, which is what causes
  an `UPDATE` to be issued for a root that would otherwise be skipped.
- `DbUpdateConcurrencyException` is translated to **409** by one shared
  mapping. Per ADR-0047 this is an infrastructure signal, so it belongs
  in middleware — not in a `try`/`catch` in each of the 18 gated
  handlers.

The interceptor lives in `ServiceDefaults`, which already carries EF
Core via `WolverineFx.EntityFrameworkCore` and is referenced by no
Domain project — so the domain stays framework-free without a new
shared project.

### Corrections to ADR-0043

1. The **Marten exemption for Overlays and Automation is withdrawn.**
   Both are EF Core and are covered by this ADR like every other
   context.
2. **Retry-once is removed.** A conflict surfaces to the caller; the
   client refetches and the human decides. Automatic retry is forbidden
   in both the backend and the SPAs.
3. The **cross-request guarantee is added** as Layer 1.

`Version` keeps its `protected set`. EF writes through the change
tracker, not the property setter, so domain encapsulation is preserved.

### Scope

Layer 2 applies to all nine EF-backed contexts. Layer 1 applies to
operator-mutable aggregates only — 18 of the 30 command handlers.
Deliberately excluded:

- **`ReportStreamHealth`** — mutate-existing but machine-driven by the
  health watcher. No client holds a stale view, so an expected-version
  gate would be wrong. StreamDistribution has no mutating HTTP surface.
- **CameraCatalog** — create-only; no update path exists.
- **AuditObservability** — zero aggregate roots; `AuditEvent` is
  append-only. Its repository also issues raw SQL upserts and never
  calls `SaveChangesAsync`, so it is invisible to the interceptor.
- **`Event` / `DeadLetter`** — ingestion path, written once, never
  operator-edited. Layer 2 only.
- **`AuthorizeWhep`, `/rules/{name}/dry-run`, `/streams/authorize`** —
  not mutations despite their placement or verb.

## Consequences

**Positive:**

- The lost update ADR-0043 was written to prevent actually becomes
  impossible, in both the long-lived operator case and the transaction
  race.
- A conflict is a typed `Result` failure (ADR-0047) with an HTTP status
  the client can act on, not a 500 indistinguishable from a server
  fault, and it becomes visible in monitoring.
- One interceptor and one exception mapping cover all nine contexts; the
  per-handler cost is a single comparison.

**Negative:**

- **Requiring `If-Match` is a breaking API change.** Every existing
  mutating integration test needs the header. The diff is broad and
  mechanical, and belongs in the same change as the requirement.
- Error unions for 18 commands each gain a `*Stale` case. Verbose, but
  it is what ADR-0047's exhaustive typed errors cost, and it is what
  #240/#283 already specified.
- The interceptor runs on every `SaveChangesAsync` in all nine contexts.
  A bug there is system-wide — hence the unit tests are written before
  it is wired to anything.
- The SPAs must handle 409 without retrying, which is a real UX
  decision (reload-and-discard first cut) rather than a mechanical
  change.

## Alternatives Considered

- **Expected version in a request-body field — REJECTED.** Would
  require inventing request bodies for the 14 bodiless mutating
  endpoints. `If-Match` is the standards-defined mechanism and covers
  every verb uniformly.
- **Expected version as a query-string parameter — REJECTED.** Avoids
  the body problem but puts control data in the URL, pollutes logs and
  caches, and is idiomatic nowhere.
- **412 Precondition Failed for a stale version — REJECTED**, on the
  grounds given above. Recorded as a close call.
- **Postgres `xmin` as the token — REJECTED**, consistent with
  ADR-0043's original reasoning: it leaks DB semantics into the domain.
- **A shared `ConcurrencyError : ApiError` instead of per-command
  cases — REJECTED.** Handlers return `Result<T, <Command>Error>`, so a
  shared type could only be returned by widening every signature to
  `Result<T, ApiError>`, forfeiting the exhaustive matching ADR-0047
  exists for.
- **Layer 2 alone (fix the token, skip `If-Match`) — REJECTED.** It is
  the cheaper half and catches only the rare race, leaving the actual
  operator-facing lost update untouched — while *appearing* to close
  the issue. That appearance is the main risk this ADR guards against.
- **Layer 1 alone (skip the token) — REJECTED.** Leaves the genuine
  race unprotected, and leaves a concurrency token in the schema that
  does nothing, which is how this situation arose.
- **Keeping retry-once — REJECTED.** See defect 2.

## Implementation Notes

**The rollout order is load-bearing, not a preference.**

Today the token is inert, so conflicts pass silently. The moment
`Version` starts incrementing, previously-invisible conflicts become
real `DbUpdateConcurrencyException`s — and with no mapping, they surface
as **500s**. Shipping the bump ahead of the handling would convert a
silent data bug into a visible outage.

Therefore:

1. This ADR.
2. Interceptor + exception mapping + tests, **wired to nothing**.
3. Register across all nine contexts — the behaviour change.
4. Layer 1 per context, starting with Layout and Overlay together.
5. Frontend and e2e.

Steps 2 and 3 must not be separated such that a bump is active without
the mapping.

**The same hazard applies to the API contract, in the opposite
direction.** Making `If-Match` required is a breaking change for any
client that does not yet send it — and the management SPA is such a
client until its transport is updated. Requiring the header first would
return `428` on every layout mutation from the real UI until the
frontend caught up.

So step 4 splits, and the client moves first:

- **4a.** The read side returns the version (`ETag` + body field), and
  the SPA starts *sending* `If-Match`. A header the server ignores is a
  no-op, so nothing breaks.
- **4b.** The server starts *requiring* and comparing it. The client is
  already compliant.

Do not bridge this by accepting requests without `If-Match` "for now".
An optional precondition is one that never gets sent, and it is the same
silent fallback this ADR rejects — the hole would simply move from the
database to the API.

**The client passes the version explicitly, and does not cache entity
tags.** A central `ETag` store in the shared gateway client would have
to map a request URL back to the resource whose tag guards it — `POST
/layouts/{id}/revisions/2/publish` is guarded by the tag from `GET
/layouts/{id}` — and any miss would degrade to a request with no
version. Threading the version through each mutation's arguments instead
makes the type checker reject a call site that forgets, which is the
property that matters here.

**Verification is behavioural, not structural.** The two tests that
matter are a real two-`DbContext` conflict (Layer 2) and a
`GET` → mutate → stale-`ETag` mutate → 409 sequence (Layer 1). A mocked
throw proves nothing about the EF wiring, which is the specific failure
mode that produced this ADR.

Per ADR-0104, LayoutComposition and OverlayDesigner receive identical
treatment; that duplication is deliberate and must stay in lockstep.

Plan and task breakdown: `specs/012-optimistic-concurrency/`.
