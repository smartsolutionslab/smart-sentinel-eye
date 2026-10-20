# Spec 422 — Audit reads fail closed for a caller without a fab, and a by-id refusal is a 404

**Issue:** #3035, items 1, 2 and 3 (item 4 was delivered by spec 417 / #3062). Feature-level issue,
already on Project #13; no per-task issues.
**Branch:** `fix/3035-unscoped-audit-reads` (cut from `origin/develop` `308629e6`)
**Lane:** autonomous (ADR-0144), `agent:ready`. Security-sensitive (authorization): `security-reviewer`
and `/security-review` are required at phase 6.
**Decisions implemented (owner, recorded on #3035, 2026-10-19).** This spec implements them; it does not
re-decide them.

- **Item 1, cure (a):** a caller with no usable `/fabs/<fab>` claim sees **only** rows with
  `Fab == null && FabAttribution == NotApplicable`. `Unresolved` rows are visible to nobody unscoped. No new
  scope, no ADR. Reverses spec 245's pin `An_unresolved_row_is_returned_to_a_caller_with_no_usable_fab_claim`
  and the `Residual_*` adversary tests (spec 396); both reversals are authorised for this change.
- **Item 2:** `GET /audit/{id}` answers **404** for any row the caller may not read, exactly as for an
  unknown id. No `FabAuthorizationException`, no 403, no fab id in the body. Reverses the pinned 403 control
  test; authorised.
- **Item 3:** add the partial index `ON audit_events (occurred_at) WHERE fab_attribution = 'NotApplicable'`
  by migration, hypertable-aware, **unmeasured** — no live `EXPLAIN` is available; CI integration is the
  only evidence and the PR says so. The optional `CHECK ((fab_id IS NOT NULL) = (fab_attribution =
  'Resolved'))` is **not decided** and stays out.

**Predecessors:** spec 009 (audit read API), spec 245 (per-entry skip of malformed claims), spec 306
(`FabAttribution`), spec 396 / #2927 (`AuditReadScope`; scoped reads hide `Unresolved`), spec 417 (the
cross-fab read-guard tests pivot by id).
**ADRs:** ADR-0037, ADR-0047/0089 (errors), ADR-0052/0053/0054 (tests), ADR-0067 (MigrationRunner),
ADR-0070 (Minimal APIs), ADR-0101 (TimescaleDB for audit), ADR-0103 (Aspire fixture), ADR-0109, ADR-0139,
ADR-0144, **ADR-0159 §2** ("fab is a data scope, and that scope is binding"), **ADR-0161** (precedent: a
row with no attributed fab is visible to nobody; `GET /streams/{id}` answers 404 for a stream outside the
caller's fabs, indistinguishable from one that does not exist).
**Constitution:** §II, §III, §Testing.

---

## Problem — premise verified on `develop` `308629e6` (2026-10-20)

The cites in #3035 are from #2927's review; the code has moved since, so each claim was re-read.

| # | Claim | Verdict on `308629e6` |
|---|---|---|
| 1 | An unscoped caller reads every fab's `Unresolved` rows | **Holds.** `AuditReadScope.Apply` (`Application/Queries/Handlers/AuditReadScope.cs:52-59`) falls through to `source.Where(auditEvent => auditEvent.Fab == null)` when `IsScoped` is false — any attribution. `Hides` (`:61-62`) is `IsScoped && …`, so by id an unscoped caller is handed an `Unresolved` row with its payload. |
| 1a | "Usable" = a `/fabs/<fab>` group that parses as `FabIdentifier` | **Holds.** `FabClaims.AssignedFabs` (`ServiceDefaults/Authorization/FabClaims.cs:36-50`) strips `/fabs/` from tokens with that exact prefix; `AuditReadScope.ForCaller` (`:31-48`) keeps entries `FabIdentifier.From` accepts. `/fabs/munich/ops`, `/fabs`, `/fabs/`, `/fabs/MUNICH`, `/FABS/munich`, `/operators`, no groups: all unscoped. Pinned as such by `AuditReadEndpointAdversarialTests.UnscopedGroupClaims`. |
| 1b | Which paths fall through | **Two, not three.** Search without `fabId` (`SearchAuditQueryHandler.ScopeToFabs`, `:74-84` → `ForCaller`) and by id (`GetAuditEventQueryHandler`, `:23` → `ForCaller(...).Hides`). The timeline always uses `AuditReadScope.Named(fab)` behind a mandatory, guarded `fabId` (`AuditEndpoints.cs:150-159`), so it is never unscoped. `AuditReadScope` has no other caller (grep: the three handlers and the query's doc comment). `AuditEndpoints` maps exactly three `GET`s; no export, hub or SSE reads audit rows. |
| 2 | By id, a `Resolved` row of another fab answers 403 naming the fab | **Holds.** The handler returns the row (`Hides` is false for a non-null fab); `AuditEndpoints.GetSingle` (`:219-222`) then calls `fabGuard.EnsureAccessAsync(user, row.Fab, …)`, which throws `FabAuthorizationException(fabId)` → 403 `RESOURCE_FAB_NOT_AUTHORIZED` carrying the row's fab. An unknown id answers 404 `AUDIT_EVENT_NOT_FOUND`. The same 403 reaches an **unscoped** caller for any `Resolved` row. Pinned by `AuditReadEndpointAdversarialTests.A_resolved_row_of_another_fab_is_still_refused_by_the_guard_not_hidden`. |
| 3 | No index serves `fab_attribution = 'NotApplicable'` | **Holds.** `AuditEventConfiguration.ConfigureIndexes` (`Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs:155-180`) declares `ux_audit_event_identifier`, `ix_audit_actor_occurred`, `ix_audit_fab_occurred`, `ix_audit_kind_occurred`, `ix_audit_resource_occurred`; none filtered, none on `fab_attribution`. Migrations: `InitialAuditObservability`, `AuditIngestBreakdownColumns`, `AuditFabAttribution` (column + backfill only). |

PR #3067 (#3023, spec 421) touches CameraCatalog URL parsing only; it does not intersect this change.

## The rule (R2) — replaces spec 396's R1 for the unscoped bucket only

> A read's **fab set** is the fab it names (search with `fabId`, timeline), or otherwise the caller's
> **usable** fab claims (unchanged, spec 245).
>
> A row is visible **iff** it is fab-neutral by design (`Fab == null` and `FabAttribution ==
> NotApplicable`) **or** it has a fab that is in the fab set.

One rule for every read; with an empty fab set the second disjunct is simply empty. The scoped bucket is
**unchanged** from spec 396. What changes is the unscoped bucket (item 1) and how the single-row read
answers a row the rule does not admit (item 2).

| Row | Scoped read | Unscoped read (no usable claim) |
|---|---|---|
| `Resolved`, fab in the fab set | visible (unchanged) | — (fab set empty) |
| `Resolved`, other fab | search/timeline: hidden (unchanged). **By id: 404, was 403** | search: hidden (unchanged). **By id: 404, was 403** |
| `NotApplicable` | visible (unchanged) | visible (unchanged — #1300 stays closed) |
| `Unresolved` | hidden; by id 404 (unchanged) | **hidden; by id 404 — was visible** |

Not changed, and stated so nobody reads it into R2: a request that **names** a fab the caller does not hold
(`GET /audit?fabId=berlin`, `GET /audit/{kind}/{id}?fabId=berlin`) is still refused 403
`RESOURCE_FAB_NOT_AUTHORIZED` by the guard. That refusal echoes the caller's own input and reveals nothing
about stored rows, so it is not an existence oracle and item 2 does not reach it. A caller without
`sse.audit.read` still gets the framework's 403 on every route.

## Why no ADR, and no constitution change

- **Item 1** applies ADR-0159 §2: access is decided by fab-group membership, and a caller with no usable
  membership holds no fab. ADR-0161 is the precedent for the direction (no attributed fab ⇒ visible to
  nobody). No deployment-wide auditor class exists (ADR-0159 §2, ADR-0161, spec 245, spec 396 A1); creating
  one would be a new scope and need an ADR — **out of scope**, and the lane blocks if a reviewer reads R2
  as one.
- **Item 2** is the 404-for-out-of-scope convention already used by `GET /streams/{id}` (spec 016 FR-006,
  cited in ADR-0161) and by spec 396 for `Unresolved` rows. It extends an existing answer to more rows.
- **Item 3** is an index under ADR-0101's store, added by the ADR-0067 MigrationRunner. No column, no
  constraint, no data change.
- No `Shared.Contracts`, envelope, `Scope.cs`, realm, `ServiceDefaults` or AppHost change.

## Locked tech choices

Nothing new is chosen.

| Concern | Choice | Where |
|---|---|---|
| Audit store | PostgreSQL + TimescaleDB hypertable `audit_events` (1-month chunks, compression after 30 days), EF Core, `fab_attribution varchar(16)` | ADR-0101; spec 306 |
| Migrations | EF-generated migration in `AuditObservability.Infrastructure`, applied by the MigrationRunner before services start | ADR-0067 |
| Read API | Minimal APIs, `sse.audit.read`, `IFabAuthorizationGuard` for a named fab | ADR-0070; spec 008 FR-019 |
| Errors | existing `AUDIT_EVENT_NOT_FOUND` (404) | ADR-0047/0089 |
| Tests | xUnit + Shouldly + hand-written fakes/builders; in-process endpoint driver; integration via `AspireFixture` (CI) | ADR-0052/0053/0054/0103 |

---

## User stories

### US1 (P1) — a caller without a usable fab claim reads no fab's audit rows (item 1)

**As** the system, **I must not** hand a caller whose groups contain no usable `/fabs/<fab>` claim any audit
row except a fab-neutral one, on search or by id, **so that** a misconfigured or sub-group-only account
(`/fabs/munich/ops`) sees *less* than a correct member, not more — as every other context already fails
closed for such a caller.

### US2 (P1) — a by-id read of a row the caller may not read is indistinguishable from an unknown id (item 2)

**As** an operator of `/fabs/munich`, **I must not** learn from `GET /audit/{id}` that a berlin row exists, or
that it is berlin's, **so that** audit identifiers are not an existence or ownership oracle.

### US3 (P2) — the fab-neutral disjunct has an index (item 3)

**As** the audit store, **I** carry `ix_audit_not_applicable_occurred` on `audit_events (occurred_at) WHERE
fab_attribution = 'NotApplicable'`, **so that** the `NotApplicable` disjunct of every scoped search — and
the whole of an unscoped one after US1 — need not walk `occurred_at` across every chunk. Unmeasured by
owner decision.

US1 and US2 are one vertical: they change the same rule type, the same by-id handler and endpoint, and the
same test files (plan §6). US3 is file-disjoint and independently shippable, but rides the same PR because
the owner scoped one issue and there is one branch (one branch, one index).

---

## Acceptance scenarios

Seed for the scenarios: on one resource, a `munich` `Resolved` row **M**, a `berlin` `Resolved` row **B**, an
`Unresolved` row **U** (fab null, `FabScope.Owned`) and a `NotApplicable` row **N** (fab null,
`FabScope.Neutral`).

### Happy path

```gherkin
Scenario: an unscoped search returns only the fab-neutral row
  Given a caller whose groups are <groups>
  When it calls GET /audit without fabId
  Then the response is 200
  And the rows are exactly [N]
  Examples: <groups> = [] | ["/fabs"] | ["/fabs/"] | ["/fabs/munich/ops"] | ["/fabs/MUNICH"]
                      | ["/FABS/munich"] | ["fabs/munich"] | ["/fabs/munich/"] | ["/operators"]

Scenario: an unscoped caller reads the fab-neutral row by id
  Given a caller with no usable fab claim
  When it calls GET /audit/{N}
  Then the response is 200 and carries N

Scenario: a scoped caller's reads are unchanged
  Given a caller in /fabs/munich
  Then GET /audit returns exactly [M, N]
  And GET /audit/{M} and GET /audit/{N} answer 200
  And GET /audit/stream/{resource}?fabId=munich returns exactly [N, M]

Scenario: a multi-fab caller reads every fab it holds by id
  Given a caller in /fabs/munich and /fabs/berlin
  Then GET /audit/{B} answers 200

Scenario: the partial index exists after migration
  Given the AuditObservability migrations have run
  Then pg_indexes has ix_audit_not_applicable_occurred on audit_events
  And its definition is on (occurred_at) with predicate fab_attribution = 'NotApplicable'
```

### Conflict (row vs caller's fab set)

```gherkin
Scenario: an unscoped caller is answered for an Unresolved row exactly as for an unknown id
  Given a caller with no usable fab claim
  When it calls GET /audit/{U} against the seeded store, and GET /audit/{U} against an empty store
  Then both answer 404 AUDIT_EVENT_NOT_FOUND
  And the two status codes, bodies and Location headers are byte-identical
  And the body contains no payload text

Scenario: a Resolved row of another fab is answered exactly as an unknown id
  Given a caller in /fabs/munich
  When it calls GET /audit/{B}
  Then the response is 404 AUDIT_EVENT_NOT_FOUND, byte-identical to an unknown id's
  And no FabAuthorizationException is raised
  And the body does not contain "berlin"

Scenario: an unscoped caller is answered for any Resolved row exactly as an unknown id
  Given a caller with no usable fab claim
  When it calls GET /audit/{M}
  Then the response is 404 AUDIT_EVENT_NOT_FOUND, byte-identical to an unknown id's
  And the body does not contain "munich"

Scenario: a row whose attribution contradicts its fab fails closed (unchanged)
  Given a berlin row relabelled NotApplicable
  Then a munich search does not return it, and an unscoped search does not return it
```

### Bad request

```gherkin
Scenario: an empty Guid is still refused before any read
  When any caller calls GET /audit/00000000-0000-0000-0000-000000000000
  Then the response is 400 AUDIT_INVALID_INPUT (unchanged)

Scenario: a malformed named fab is still a client error
  When a caller calls GET /audit?fabId=NOT_A_FAB
  Then the response is 400 AUDIT_INVALID_INPUT (unchanged)
```

### Auth

```gherkin
Scenario: naming a fab the caller does not hold is still refused by the guard
  Given a caller with no usable fab claim, or a caller in /fabs/munich only
  When it calls GET /audit?fabId=berlin (or ?fabId=munich for the unscoped caller)
  Then FabAuthorizationException → 403 RESOURCE_FAB_NOT_AUTHORIZED (unchanged)

Scenario: the timeline still requires a held, named fab
  When a caller in /fabs/munich calls GET /audit/stream/{resource}?fabId=berlin
  Then the response is 403 RESOURCE_FAB_NOT_AUTHORIZED (unchanged)

Scenario: a token without sse.audit.read is refused on every route
  Then GET /audit, GET /audit/{id} and the timeline answer 403 (unchanged)

Scenario: an anonymous caller is refused
  Then every route answers 401 (unchanged)
```

## Functional requirements

- **FR-001** `AuditReadScope.Apply` with no usable fab admits exactly `Fab == null && FabAttribution ==
  NotApplicable`. The scoped branch is unchanged.
- **FR-002** The by-id read admits a row iff R2 admits it, using the same type, for scoped and unscoped
  callers alike: a null-fab row iff `NotApplicable`; a non-null-fab row iff its fab is in the caller's usable
  fabs. Every other row answers the same `AUDIT_EVENT_NOT_FOUND` value an unknown id answers.
- **FR-003** `GET /audit/{id}` no longer calls `IFabAuthorizationGuard`; the handler is the single authority
  for that route. Its OpenAPI metadata keeps `403` (scope refusal) and `404`.
- **FR-004** Search with `fabId` and the timeline keep the guard and their 403 for an unheld named fab.
- **FR-005** `AuditEventConfiguration` declares `ix_audit_not_applicable_occurred` on `OccurredAt` with
  filter `fab_attribution = 'NotApplicable'`; a generated migration creates it (plain `CREATE INDEX`, no
  `CONCURRENTLY`) and its `Down` drops it; the model snapshot matches (`HasPendingModelChanges() == false`).
- **FR-006** No other schema object changes: no `CHECK` constraint, no column, no data update, no change to
  existing indexes. Any EF-emitted churn on existing objects is removed by hand (precedent:
  `AuditIngestBreakdownColumns`).
- **FR-007** Doc comments that state the old unscoped rule (`AuditReadScope`, `GetAuditEventQuery`,
  `SearchAuditQuery`, `AuditEndpoints.GetSingle`, the test class headers) are corrected in the same commit as
  the code they describe.

## Success criteria

- **SC-1** The handler, endpoint and SQL-translation tests listed in plan §5.4 are observed **red** on the
  unmodified code and green after, with their output quoted in the PR (ADR-0139).
- **SC-2** Every retargeted test in plan §5.1 is named in the PR with its before/after.
- **SC-3** The tests in plan §5.2 pass **unmodified** before and after.
- **SC-4** `git diff --stat` on `src/AuditObservability/Infrastructure/Persistence/Migrations/` shows one new
  migration pair and a modified snapshot, nothing else.
- **SC-5** CI's integration shards pass the retargeted and new integration facts; their `Passed` lines are
  read from the trx artifact, not inferred from a green bucket.

## Independent end-to-end test procedure

Docker is down on the authoring machine; the in-process steps are the local evidence, the Aspire steps are
CI's.

1. **In-process (local):** `dotnet test tests/AuditObservability.Application.Tests` and
   `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~Audit"`. The endpoint adversarial tests
   drive the real `MapAuditEndpoints`, the real `FabClaims` and the real `DefaultFabAuthorizationGuard`
   through `InProcessEndpointDriver` with a `ClaimsPrincipal` per group spelling; the translation tests run
   the real handlers through Npgsql with the connection suppressed and capture the SQL.
2. **Aspire (CI):** `CrossFabReadGuardIntegrationTests` (shard 2) and `UnresolvedFabAuditRowIntegrationTests`
   + `NeutralFabRetentionRowIntegrationTests` (shard 4) boot the stack, run the new migration through the
   MigrationRunner against `timescale/timescaledb:2.27.1-pg17`, seed rows and read them over HTTP as
   `admin@munich.test` / the berlin operator; the index fact reads `pg_indexes`.
3. **If Docker is available to phase 5:** boot the AppHost, `GET /audit/{a berlin row id}` as the munich admin
   → 404 `AUDIT_EVENT_NOT_FOUND`; `\d audit_events` shows the partial index. Otherwise phase 5 records that
   CI is the only end-to-end observation.

## Latency-budget impact

**N/A** for the event→overlay budget (§IV): audit reads and the audit store are not on that path. Audit's
own NFRs: item 3 adds one partial-index maintenance step per inserted `NotApplicable` row (NFR001 ingest)
and may change plans for scoped/unscoped searches (NFR002). Neither is measured here (owner decision); the
existing CI NFR001/NFR002 tests are the only evidence and they seed a single fab, so they cannot show the
selective case #3035 describes.

## Out of scope

- The `CHECK ((fab_id IS NOT NULL) = (fab_attribution = 'Resolved'))` constraint — not decided.
- A deployment-wide auditor scope — a new authorization class; needs an ADR.
- Refusing an unscoped caller outright (cure (b), `AUDIT_FAB_REQUIRED`) — not chosen.
- `EXPLAIN (ANALYZE, BUFFERS)` on a selective seed — not available; owner accepted unmeasured.
- The `ShouldContain("munich")` over the shared table in
  `An_empty_fab_on_the_audit_search_spans_the_callers_fabs` (noted on #3035) — a test-isolation nit, not
  one of the decided items.
- Frontend: `management-web` treats any non-2xx from `getAuditEvent` as an error; no change needed.

## Assumptions (marked)

- **A1 — `CREATE INDEX` on a hypertable with compressed chunks.** TimescaleDB propagates `CREATE INDEX` on the
  hypertable to every chunk and does not support `CONCURRENTLY` on hypertables. Whether 2.27 builds the
  index across **columnstore-compressed** chunks without error is **not verified**: CI boots a fresh
  database with no compressed chunk, so CI proves only the empty/young-chunk path. A long-lived dev volume
  (run-mode containers keep months of history; compression starts at 30 days) is where it would surface, as
  a MigrationRunner failure at boot. If phase 4/5 can reach Docker, run the migration once against such a
  volume; if it fails, **block** — decompressing chunks in a migration is a design decision, not this spec's.
- **A2 — `CREATE INDEX` locks inserts while it builds.** Acceptable because the MigrationRunner runs before
  the services start (ADR-0067); there is no production deployment.
- **A3 — the guard on `GET /audit/{id}` is dead after FR-002.** A `Resolved` row reaches the endpoint only if
  its fab is in the caller's usable fabs, i.e. the caller holds exactly the token `/fabs/<fab>` the guard
  looks for (`FabIdentifier`'s grammar has one spelling). Removing it (FR-003) therefore changes no answer
  for an admitted row and closes the 403 path for a refused one. Flagged for the security reviewer.
