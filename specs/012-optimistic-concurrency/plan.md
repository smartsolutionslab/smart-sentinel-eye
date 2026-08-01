# Implementation Plan: 012 — Optimistic Concurrency (make ADR-0043 real)

**Branch:** `012-optimistic-concurrency` | **Date:** 2026-08-01 |
**Requirement source:** [#1154](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1154)
(as corrected) + ADR-0043

**Status:** Draft (Phase 2 — Plan)

**Input:** No `spec.md`. This is cross-cutting remediation of an
existing accepted decision (ADR-0043), not a new capability, so the
requirement source is the corrected issue rather than a Phase-1 spec.
Flagged at the Phase-2 gate.

## Summary

ADR-0043 specifies optimistic concurrency with an explicit `Version` on
every aggregate root. **None of it currently works.** The column is
mapped as a concurrency token in all ten configurations, but nothing
ever increments it, so EF emits `WHERE version = 0` against a row whose
version is permanently `0`. Two concurrent writers both match and the
last write silently wins.

This plan makes the guarantee real, in two distinct layers that protect
against two different things:

- **Cross-request** (the operator-facing case): the aggregate version
  travels to the client and back on every mutating request. Catches
  "operator A opens the editor, B opens it, A saves, B saves a minute
  later over A's work" — the scenario ADR-0043 describes and the one an
  EF token can never see.
- **In-transaction** (the race case): the token actually moves, so two
  overlapping database transactions can no longer both commit. Catches
  requests microseconds apart.

Scope is all nine EF-backed contexts plus both SPAs.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Persistence | EF Core on Postgres, per-context DB | ADR-0009, ADR-0071 |
| Concurrency | Optimistic, explicit `Version` on the aggregate root | ADR-0043 (amended by ADR-0113, below) |
| Errors | `Result<T, Error>`; per-command sealed-record unions | ADR-0047 |
| Error → HTTP | `ApiError(Code, Message, Status)`; status read off the record | ADR-0089 |
| Argument guards | `Ensure.That(x).IsNotNull()` | ADR-0105 |
| Revisioned pair | Layout/Overlay duplication is deliberate; mirror every lifecycle change | ADR-0104 |
| Tests | xUnit + Shouldly + Moq; integration via `AspireFixture` | ADR-0052, ADR-0103 |
| Frontend | RTK Query in both SPAs | ADR-0075 |
| E2E | Playwright at `/e2e` | ADR-0108 |

## Current state (verified against `develop` @ e8c52e2)

- `AggregateRoot.Version` (`src/Shared.Kernel/AggregateRoot.cs:16`) is
  **never assigned anywhere in `src/`**.
- Ten configurations map it `.IsConcurrencyToken()`; the mapping is
  identical in each.
- `DbUpdateConcurrencyException` appears nowhere in `src/` or `tests/`.
- No EF interceptor exists anywhere — no `AddInterceptors`, no
  `ISaveChangesInterceptor`. There is no seam to hang this on.
- Nine `DbContext`s, no shared base class or interface. All registered
  via `AddDbContextFactory`; the scoped context comes from Wolverine's
  `UseEntityFrameworkCoreTransactions()`
  (`src/ServiceDefaults/WolverineDefaults.cs:78`).
- Eleven repositories; eight share a byte-identical `SaveAsync`.
- `Version` is exposed in **no** DTO, and there is no `If-Match`,
  `ETag`, or `expectedVersion` anywhere in `src/` or `apps/`.
- 51 error hierarchies derive from `ApiError`; there is no shared
  derived type.
- `ApiErrorResults.ToProblem()` (`src/ServiceDefaults/ApiErrorResults.cs:15-20`)
  reads the status off the record, so a 409 needs **no endpoint change**.

## Two properties of EF concurrency tokens this design depends on

1. EF puts the token's **OriginalValue** in the `WHERE` clause and its
   **CurrentValue** in the `SET`. Incrementing only CurrentValue
   therefore produces exactly
   `UPDATE … SET version = 1 WHERE id = @id AND version = 0`.
2. Setting a property's CurrentValue on an `Unchanged` entry transitions
   that entry to `Modified`. This is how a root whose own columns did
   not change gets an `UPDATE` issued at all — which matters because
   **when only an owned child row changes, EF does not touch the root
   row, so the root's token is never consulted.** Two aggregates are
   affected, and they are the two that matter most:

   | Aggregate | Owned collection | Table |
   |---|---|---|
   | `Layout` | `Revisions` → `Tiles` | `layout_revisions`, `layout_revision_tiles` |
   | `Overlay` | `Revisions` | `overlay_revisions` |

   (`Variable.BooleanLabels` is `OwnsOne` flattened onto `variables`, so
   it updates the root row and is unaffected.)

## Sequencing hazard — read before implementing

**The version bump must not ship ahead of the handling.** Today the
token is inert, so conflicts pass silently. The moment `Version` starts
incrementing, previously-invisible conflicts become real
`DbUpdateConcurrencyException`s — and with no catch, they surface as
**500s**. A global bump landed ahead of per-context handling would
convert a silent data bug into a visible outage.

Therefore the bump (Phase B) and the fallback mapping (Phase C) land in
the **same** change. No intermediate commit may leave the bump active
without the mapping.

## Design

### Where each failure is caught

The two layers surface differently, which keeps per-handler boilerplate
near zero:

| Failure | Frequency | Detected | Surfaced as |
|---|---|---|---|
| Client sent a stale version | Common; operator-facing | In the handler, after load, **before** mutating | Typed `*Stale` case on the command's error union → 409 |
| Two transactions genuinely raced | Rare | EF, at `SaveChangesAsync` | `DbUpdateConcurrencyException` → shared mapping → 409 Problem Details |

The common case is a typed `Result` failure exactly as ADR-0047
requires, and it is what satisfies #240/#283's `LayoutRevisionStale`.
The rare case is an infrastructure signal, which ADR-0047 already
assigns to middleware — so it does **not** need a `try`/`catch` in ~40
handlers.

### A — ADR-0113 (amends ADR-0043)

ADR-0043 has three defects that must be recorded before code changes:

1. It states that Overlays and Automation are Marten-backed and get the
   guarantee free from stream versions. **Both are EF Core** with the
   inert token. The exemption it grants them is not real.
2. It mandates handlers "retry once on `ConcurrencyException` and then
   surface a `Result<T, Conflict>`". Retry-once is **wrong for a stale
   cross-request version** — re-applying the mutation to freshly-loaded
   state is precisely the silent overwrite this work exists to prevent.
   Drop it.
3. It is silent on the cross-request guarantee, which is the one
   operators actually experience.

ADR-0113 records the two-layer design, the transport decision, and
supersedes those three points.

### B — Version bump (in-transaction layer)

`AggregateVersionInterceptor : SaveChangesInterceptor` in
`src/ServiceDefaults/Persistence/`. ServiceDefaults already carries EF
Core transitively via `WolverineFx.EntityFrameworkCore`, and no Domain
project references it, so this respects "Domain has no framework refs"
without a new project. Add an explicit `Microsoft.EntityFrameworkCore`
`PackageReference` rather than leaning on the transitive one.

On `SavingChangesAsync`, for each tracked entry whose entity is an
aggregate root:

- skip `Added` (version starts at 0) and `Deleted`;
- bump when the entry is `Modified`, **or** when any owned descendant
  is `Added`/`Modified`/`Deleted` (traverse the root's owned
  navigations recursively);
- bump by setting `entry.Property(nameof(Version)).CurrentValue` to
  `OriginalValue + 1`, which leaves the `WHERE` predicate intact and
  promotes an `Unchanged` root to `Modified`.

Detecting "is an aggregate root" needs a non-generic handle, since
`AggregateRoot<TIdentifier>` is generic. Add a marker interface to
`Shared.Kernel` mirroring the existing `IValueObject<T>` convention;
`AggregateRoot<TIdentifier>` implements it. `Version` keeps its
`protected set` — EF writes through the change tracker, not the setter,
so domain encapsulation is untouched.

Registration: `.AddInterceptors(...)` in the options lambda of each
`Add<Context>Persistence`. Nine sites, plus two irregularities —
CameraCatalog inlines its registration into
`CameraCatalogInfrastructureModule.cs:32-46`, and AuditObservability
hand-registers a second scoped context at
`AuditObservabilityPersistenceModule.cs:34-36` (see the comment at
:22-31 explaining why a second `AddDbContext` breaks MigrationRunner —
the interceptor must go on both registrations).

`AuditEventRepository` buffers rows and issues raw
`ExecuteSqlInterpolatedAsync` upserts, never calling
`SaveChangesAsync`. It bypasses the change tracker, is invisible to the
interceptor, and needs no version handling. Explicitly out of scope.

### C — Shared `DbUpdateConcurrencyException` mapping

One place that turns the rare true race into a 409 Problem Details
response, consistent with `ToProblem()`'s shape. Lands in the same
change as B, per the sequencing hazard.

### D — Cross-request version (operator-facing layer)

**Transport: `If-Match`.** Several mutating endpoints are bodiless
POSTs (`/layouts/{id}/draft`, `…/publish`, `…/revert`), so a body field
would need a body invented for them; a header covers every verb
uniformly. The version is the entity tag.

- Read DTOs for operator-mutable aggregates gain `Version`, and
  responses carry it as `ETag`.
- Mutating endpoints read `If-Match` and pass the expected version into
  the command.
- Handlers compare it to the loaded aggregate **before** mutating and
  return the typed `*Stale` case on mismatch.
- A mutating request with **no** `If-Match` is rejected. We control
  both SPAs, so there is no external client to break, and a silent
  fallback to "no concurrency control" would recreate today's bug.
  Existing integration tests that mutate will need updating — this is
  expected, not incidental.

Applies to operator-mutable aggregates only: `Layout`, `Overlay`,
`Camera`, `Stream`, `Variable`, `Rule`, `RegisteredClient`,
`WebhookIntegration`. `Event` and `DeadLetter` are ingestion-path,
written once, never operator-edited — they get the B/C layers (harmless
and uniform) but no `If-Match` plumbing.

**Open point for review, not blocking:** strict RFC 7232 says a failed
`If-Match` is `412 Precondition Failed`. This plan returns **409** to
match #240/#283's `LayoutRevisionStale` and the existing convention
(`InvalidStateTransition` is already 409). Worth a reviewer's opinion.

### E — Error cases

Add a nested `*Stale` case to the error union of every mutating command
across the nine contexts, following the exact shape of
`PublishRevisionError.InvalidStateTransition`
(`PublishRevisionErrors.cs:25`). Status `HttpStatusCode.Conflict`, so
no endpoint mapping changes. Per ADR-0104, Layout and Overlay get
identical treatment deliberately.

LayoutComposition's endpoints do not currently declare
`.ProducesProblem(StatusCodes.Status409Conflict)`; add it where a 409
becomes reachable.

### F — Frontend (both SPAs)

- RTK Query: capture `ETag` from reads, send `If-Match` on mutations.
- Handle 409: refetch, and tell the operator their copy is stale rather
  than failing silently or retrying. **A retry would overwrite the
  other operator** — the exact bug this work removes.
- The conflict UX needs a decision at implementation time: reload-and-
  discard vs. show-a-diff. Reload-and-discard is the smaller honest
  first cut.

### G — Migrations

None. The `version` column already exists in every table.

## Verification ("done" is observable, not "it compiles")

| Level | Test | Proves |
|---|---|---|
| Unit | Interceptor bumps on direct modify; bumps root when only an owned child changed; does not bump on `Added`/`Unchanged` | The root-row gap (the subtle half of B) |
| Integration | Two `DbContext`s from the factory load the same aggregate, both save; second fails | A real EF conflict — **not** a mocked throw, which would prove nothing about the wiring |
| Integration | `GET` → mutate → mutate again with the stale `ETag` → 409 | The cross-request layer, deterministically, without racing HTTP |
| Integration | Mutating request with no `If-Match` is rejected | The fallback can't silently reopen the hole |
| E2E | Two browser contexts edit one layout; second save is refused and reloads | The operator-facing scenario end to end |

The first two are the ones that matter: steps B and C are **only**
observable through them.

## Risks

- **Largest risk is the sequencing hazard above** — a bump without
  handling is worse than today.
- Requiring `If-Match` breaks every existing mutating integration test
  at once. Expect a large, mechanical test diff in the same change.
- The interceptor runs on every `SaveChangesAsync` in all nine
  contexts. A bug here is system-wide, which is the argument for the
  unit tests in the table above being written first.
- Nine contexts + two SPAs in one body of work is a large PR. If review
  proves unwieldy, the natural split is B+C (all contexts, one change)
  then D+E+F per context — but **not** B alone.

## Phase gates

- **Phase 2 (this document):** no `spec.md` exists; confirm that is
  acceptable for remediation work, or ask for one.
- **Phase 3:** tasks + issues; #240, #283, #843 fold in as children.
- **Phase 4:** implement. ADR-0113 lands first, before code.
- **Phase 5:** the verification table, run and cited.
