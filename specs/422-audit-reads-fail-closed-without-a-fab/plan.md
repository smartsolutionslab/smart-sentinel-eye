# Plan 422 — Audit reads fail closed for a caller without a fab, and a by-id refusal is a 404

Spec: [spec.md](./spec.md). Issue #3035 items 1-3. Lane: autonomous (ADR-0144). Premise verified on
`develop` `308629e6` — spec §Problem.

## 1. Bounded context and layers

**AuditObservability only.**

| Layer | Change | Item |
|---|---|---|
| Domain | none | — |
| Application | `AuditReadScope` (unscoped branch of `Apply`; `Hides` → the full R2 test); doc comments on `GetAuditEventQuery`, `SearchAuditQuery`; `GetAuditEventQueryHandler` comment | 1, 2 |
| Api | `AuditEndpoints.GetSingle` drops the `IFabAuthorizationGuard` parameter and the post-read guard call | 2 |
| Infrastructure | `AuditEventConfiguration.ConfigureIndexes` + one generated migration + snapshot | 3 |

No `Shared.Kernel`, `Shared.Contracts`, `ServiceDefaults`, AppHost, realm or frontend change. No message, no
domain or integration event. No cross-context reference; NetArchTest rules are unaffected.

## 2. Design — items 1 and 2 (`AuditReadScope`)

`src/AuditObservability/Application/Queries/Handlers/AuditReadScope.cs` stays the one rule. Two edits, in
two red/green pairs so each item is separately observable:

**Item 1 — `Apply`, unscoped branch.**

```text
before:  IsScoped ? <scoped disjunction> : source.Where(e => e.Fab == null)
after:   IsScoped ? <scoped disjunction> : source.Where(e => e.Fab == null && e.FabAttribution == FabAttribution.NotApplicable)
```

Keep the two branches rather than folding the empty case into the scoped expression: it avoids sending an
empty `@allowed` array, and keeps the scoped SQL that `AuditReadScopeTranslationAdversarialTests` pins
byte-for-byte unchanged. `Hides` in this commit loses its `IsScoped &&` guard only:
`row.Fab is null && row.FabAttribution != FabAttribution.NotApplicable`.

**Item 2 — the by-id test becomes R2 in full.** Replace `Hides(row)` with the complement of the admission
rule, for scoped and unscoped alike:

```text
admits(row) = row.Fab is null ? row.FabAttribution == NotApplicable : fabs.Contains(row.Fab)
```

The handler answers `AuditEventNotFound(requested id)` when `row is null || !admits(row)` — the same value
for both, so the 404 body cannot differ (spec 396 already proves byte-equality for `Unresolved`; this extends
it). Name the member for what it decides (`Admits`) and drop `Hides`; the only caller is
`GetAuditEventQueryHandler`. Argument guard on `row`: `Ensure.That(row).IsNotNull()` (ADR-0105).

`AuditEndpoints.GetSingle`: remove `[FromServices] IFabAuthorizationGuard fabGuard` and the
`if (row.Fab is not null) await fabGuard.EnsureAccessAsync(...)` block with its comment; return
`Results.Ok(result.Value)`. Keep `.ProducesProblem(403)` (a token without `sse.audit.read` still gets 403;
`EndpointScopeDeclarationTests` requires the declaration on a scoped mapping). Spec A3 records why the guard
is dead after item 2.

Invariant the rule relies on, unchanged: `AuditEvent.From` sets `Resolved ⇔ Fab != null`; the spec 306
backfill wrote the same. A contradicting row fails closed on every path (the null-fab disjunct requires
`Fab == null`; a non-null fab is judged by the fab), pinned by the two `*_mislabelled_*` adversary tests,
which stay green unmodified.

## 3. Design — item 3 (partial index)

**EF configuration** (`AuditEventConfiguration.ConfigureIndexes`), mirroring the nine filtered indexes other
contexts already declare (`HasFilter("state <> 'Archived'")` etc.):

```csharp
builder.HasIndex(auditEvent => auditEvent.OccurredAt)
    .HasDatabaseName("ix_audit_not_applicable_occurred")
    .HasFilter("fab_attribution = 'NotApplicable'");
```

with a one-line *why* comment (the fab-neutral disjunct of the read scope; #3035). Name follows the
`ix_audit_<what>_occurred` convention. The filter literal is the stored string of
`FabAttribution.NotApplicable` (`HasConversion<string>()`, spec 306), exactly what the translated predicate
compares against (`a.fab_attribution = 'NotApplicable'`, pinned by the translation tests).

**Migration.** `dotnet ef migrations add AuditNotApplicableOccurredIndex` in
`src/AuditObservability/Infrastructure` (design-time factory: `DesignTimeDbContextFactory`; no database
needed). Expected `Up`: one `migrationBuilder.CreateIndex(name: "ix_audit_not_applicable_occurred", table:
"audit_events", column: "occurred_at", filter: "fab_attribution = 'NotApplicable'")`; `Down`: one
`DropIndex`. Hypertable rules applied:

- **No `CONCURRENTLY`.** TimescaleDB does not support concurrent index creation on a hypertable; EF's
  `CreateIndex` emits a plain `CREATE INDEX`, which TimescaleDB propagates to every chunk. Do not hand-write
  `migrationBuilder.Sql(... CONCURRENTLY ...)` or `suppressTransaction`.
- **Not unique**, so TS103 (a unique index must contain `occurred_at`) does not apply — it contains it anyway.
- **Remove any churn by hand.** The `AuditIngestBreakdownColumns` precedent: EF re-emitted
  `PK_audit_events` / `ux_audit_event_identifier` drop-and-recreate on a provider bump, and its generated
  `Down` recreated them non-composite, which TimescaleDB refuses. The new migration must contain the one
  index and nothing else; `HasPendingModelChanges()` must stay false after the churn is removed.
- **A comment in the migration** recording: unmeasured by owner decision (#3035, 2026-10-19); no `CHECK`
  (not decided); compressed-chunk behaviour unverified (spec A1).
- The snapshot (`AuditObservabilityDbContextModelSnapshot.cs`) gains the index with `HasFilter`.

**Synergy with item 1:** after item 1 an unscoped search's whole predicate is
`fab_id IS NULL AND fab_attribution = 'NotApplicable'`, which the partial index's predicate implies, so it is
the read the index serves best. Not measured.

## 4. Behaviour per path (after)

| Path | Caller | Before | After |
|---|---|---|---|
| `GET /audit` (no `fabId`) | scoped | own fabs + `N` | unchanged |
| `GET /audit` (no `fabId`) | unscoped | every null-fab row (`U` + `N`) | **`N` only** |
| `GET /audit?fabId=x` | holds `x` | `x` + `N` | unchanged |
| `GET /audit?fabId=x` | does not hold `x` (incl. unscoped) | 403 `RESOURCE_FAB_NOT_AUTHORIZED` | unchanged |
| `GET /audit/{kind}/{id}?fabId=x` | holds `x` | `x` + `N` | unchanged (never unscoped) |
| `GET /audit/{kind}/{id}?fabId=x` | does not hold `x` | 403 | unchanged |
| `GET /audit/{id}` → own-fab `Resolved` | scoped | 200 | unchanged |
| `GET /audit/{id}` → other-fab `Resolved` | scoped | **403, body names the fab** | **404 `AUDIT_EVENT_NOT_FOUND`** |
| `GET /audit/{id}` → any `Resolved` | unscoped | **403, body names the fab** | **404** |
| `GET /audit/{id}` → `NotApplicable` | any | 200 | unchanged |
| `GET /audit/{id}` → `Unresolved` | scoped | 404 | unchanged |
| `GET /audit/{id}` → `Unresolved` | unscoped | **200 with payload** | **404** |
| `GET /audit/{id}` → unknown | any | 404 | unchanged |

## 5. Tests — phase 4a (test-writer), colour declared

**Colour: red (behaviour-changing) for items 1 and 2. Red for item 3** via the model characterisation line
(a schema change is a behaviour change of the model; the expected line is observed failing before the
configuration exists). Characterisation (§5.2) is observed green before and must pass unmodified after.

Where tests run: **in-process** = `AuditObservability.Application.Tests` (handlers over LINQ-to-objects
fakes) and `Architecture.Tests` (endpoint adversarial via `InProcessEndpointDriver` with the real
`FabClaims` + `DefaultFabAuthorizationGuard`; SQL translation via Npgsql with the connection suppressed;
model characterisation; route table). No Docker. **CI-only** = `Integration.Tests` (Aspire fixture;
shards 2 and 4). Docker is down on the authoring machine, so every red that phase 4a must *observe* is an
in-process one; the integration retargets are confirmations read from CI's trx.

### 5.1 Retargeted or retired (authorised by the owner decisions; each named in the PR)

Item 1 — the unscoped pins (spec 245 / spec 396 residual):

| # | Test (file) | Before | After |
|---|---|---|---|
| R1 | `GetAuditEventScopeTests.An_unresolved_row_is_returned_to_a_caller_with_no_usable_fab_claim` (`null`, `"Munich"`) — Application | `IsSuccess`, returns `U` | renamed `An_unresolved_row_is_not_found_for_a_caller_with_no_usable_fab_claim`; `IsFailure`, `AuditEventNotFound`, equal to the unknown-id error (same shape as `An_unresolved_row_is_not_found_for_a_fab_scoped_caller`) |
| R2 | `AuditReadScopeAdversarialTests.Residual_a_caller_whose_every_claim_is_malformed_has_no_usable_fab_claim_and_searches_the_unresolved_row` — Application | `ShouldBe([U, N])` | renamed `A_caller_whose_every_claim_is_malformed_searches_only_the_not_applicable_row`; `ShouldBe([N])`; same `WhollyMalformedClaimSets` |
| R3 | `AuditReadScopeAdversarialTests.Residual_a_caller_whose_every_claim_is_malformed_reads_the_unresolved_row_by_identifier` — Application | `IsSuccess`, attribution `Unresolved` | renamed `A_caller_whose_every_claim_is_malformed_is_answered_not_found_for_the_unresolved_row`; hidden error equals unknown error (code, message, status) |
| R4 | `AuditReadEndpointAdversarialTests.Residual_a_caller_with_no_usable_fab_group_reads_the_unresolved_row` — Architecture | 200, body contains `lastError` | renamed `A_caller_with_no_usable_fab_group_is_answered_for_the_unresolved_row_byte_for_byte_what_an_unknown_identifier_answers`; 404, status/body/Location equal to unknown, `ShouldNotContain("lastError")`; same `UnscopedGroupClaims` |
| R5 | `AuditReadEndpointAdversarialTests.Residual_the_same_caller_is_refused_when_it_names_the_fab_it_belongs_to` — Architecture | throws `FabAuthorizationException` | **rename only** to `A_caller_with_no_usable_fab_group_is_refused_when_it_names_the_fab_it_belongs_to`; body unmodified (behaviour unchanged; it is no longer a "residual") |
| R6 | `AuditReadScopeTranslationAdversarialTests.A_search_with_no_usable_fab_claim_keeps_the_bare_null_fab_predicate_and_never_mentions_attribution` — Architecture | `ShouldContain("a.fab_id IS NULL")`, `ShouldNotContain("fab_attribution")` | renamed `A_search_with_no_usable_fab_claim_admits_only_null_fab_not_applicable_rows`; `where.ShouldContain("a.fab_attribution = 'NotApplicable'")`, `ShouldContain("a.fab_id IS NULL")`, `ShouldNotContain("@allowed")`, `ShouldNotContain("a.fab_id IS NULL OR")`. Capture the exact green `WHERE` once and pin it with `ShouldStartWith` as the scoped sibling does |
| R7 | `SearchAuditQueryHandlerTests.A_caller_with_no_fabs_still_sees_only_cross_fab_rows` — Application | rows `[M, U]`, asserts 1 row with null fab (that row is `U`) | renamed `A_caller_with_no_fabs_sees_only_the_not_applicable_row`; rows `[M, U, N]`; asserts exactly `[N.Id]` |
| R8 | `SearchAuditQueryHandlerTests.A_wholly_malformed_claim_set_sees_only_cross_fab_rows` (spec 245 SC-7) — Application | rows `[M, U]`, 1 null-fab row | renamed `A_wholly_malformed_claim_set_sees_only_the_not_applicable_row`; rows `[M, U, N]`; exactly `[N.Id]`; **keeps** `ShouldNotContain("munich")` (the anti-lowercasing guard); doc comment updated to point at R7 |
| R9 | `SearchAuditRefusalAndScopeCharacterisationTests.No_named_fab_and_no_usable_claim_returns_only_the_fab_less_rows` — Application | three calls expect `[U, N]` | renamed `No_named_fab_and_no_usable_claim_returns_only_the_not_applicable_row`; three calls expect `[N]`; class doc para corrected ("an `Unresolved` row is visible to no unscoped caller") |

Item 2 — the 403 pins:

| # | Test (file) | Before | After |
|---|---|---|---|
| R10 | `AuditReadEndpointAdversarialTests.A_resolved_row_of_another_fab_is_still_refused_by_the_guard_not_hidden` — Architecture | `Should.ThrowAsync<FabAuthorizationException>`, `FabId == "berlin"` | renamed `A_resolved_row_of_another_fab_answers_byte_for_byte_what_an_unknown_identifier_answers`; same setup (`/fabs/munich`, `B`, `AllRows()`); 404, equal to unknown, `ShouldNotContain("berlin")`, `ShouldNotContain("lastError")` |
| R11 | `GetAuditEventScopeTests.A_resolved_row_of_another_fab_is_returned_for_the_endpoint_guard_to_refuse` — Application | `IsSuccess`, `Fab == "berlin"` | renamed `A_resolved_row_of_another_fab_is_not_found_for_a_fab_scoped_caller`; error equals unknown |
| R12 | `GetAuditEventQueryHandlerTests.Returns_the_row_with_full_payload_for_a_known_id` — Application | caller fabs `[]`, builder default row (`munich`, `Resolved`) | **arrangement only**: caller fabs `["munich"]`; every assertion unmodified. Without it the test would read an unscoped caller's `Resolved` row, which the endpoint always refused (403) and now 404s |
| R13 | `CrossFabReadGuardIntegrationTests.A_seeded_unresolved_row_is_hidden_from_every_fab_scoped_path_and_a_seeded_not_applicable_row_is_not` :245 — **CI** | `singleBerlin` → `Forbidden`, "the Resolved-row 403 is unchanged" | `NotFound`, title `AUDIT_EVENT_NOT_FOUND`, message "a Resolved row of another fab answers as an unknown id (spec 422)"; nothing else in the test changes |
| R14 | `UnresolvedFabAuditRowIntegrationTests.Get_single_refuses_the_same_cameras_munich_row_to_that_operator` — **CI** | 403 `RESOURCE_FAB_NOT_AUTHORIZED` | renamed `Get_single_answers_not_found_for_the_same_cameras_munich_row_to_that_operator`; body = `AssertNotFoundIndistinguishableFromUnknownAsync(berlin, munichAuditIdentifier)` (the helper the sibling facts use) |
| R15 | `AuditObservabilityRouteTableCharacterisationTests` line for `/audit/{auditIdentifier:guid}` — Architecture | `GetSingle(auditIdentifier, fabGuard: IFabAuthorizationGuard [FromServicesAttribute], handler, user, cancellationToken)` | the same line without the `fabGuard` parameter. Route, scope, summary and produces lines unchanged |

Item 3:

| # | Test | Before | After |
|---|---|---|---|
| R16 | `AuditObservabilityModelCharacterisationTests.ExpectedModel` — Architecture | five index lines | adds `"  index ix_audit_not_applicable_occurred (occurred_at) where fab_attribution = 'NotApplicable'"` in name order (between `ix_audit_kind_occurred` and `ix_audit_resource_occurred` — confirm against the test's ordering) |

Header/section comments that state the old rule are corrected with their tests: `AuditReadScopeAdversarialTests`
(class summary, "Residual" section), `AuditReadEndpointAdversarialTests` (class summary, "Residual" section,
the R10 comment), `GetAuditEventScopeTests` (the `"Munich"` comment), `UnresolvedFabAuditRowIntegrationTests`
(`:374`, `:494` "no seeded principal is unscoped" stays true; leave).

### 5.2 Kept green, unmodified (characterisation of what must not move)

- Every scoped-bucket test in `AuditReadScopeAdversarialTests`, `AuditReadEndpointAdversarialTests`,
  `GetAuditEventScopeTests`, `SearchAuditQueryHandlerTests`, `SearchAuditRefusalAndScopeCharacterisationTests`
  (incl. `Naming_a_fab_hides_the_unresolved_row_whatever_the_caller_claims`, both `*_mislabelled_*` tests,
  paging/cursor walks, every-resource-kind timeline).
- `AuditReadScopeTranslationAdversarialTests`: the scoped, named, conjoined, timeline and single-row SQL
  facts (`The_single_row_read_fetches_by_identifier_and_decides_visibility_after_materialising` — the
  by-id SQL must still contain no `fab_` predicate).
- `GetAuditEventQueryHandlerTests.Returns_AuditEventNotFound_for_an_unknown_id`,
  `The_returned_row_carries_its_fab_attribution_by_name` (NotApplicable, `[]` — now also proves the cure
  does not reopen #1300 by id).
- `CrossFabReadGuardIntegrationTests` 403 facts for a **named** fab (`:31`, `:345`, `:435`) and
  `UnresolvedFabAuditRowIntegrationTests.A_cross_fab_timeline_is_still_refused` (FR-004).
- `CameraAddressAuditIntegrationTests` by-id read of the caller's own row (200).
- `EndpointScopeDeclarationTests`, `AuditObservabilityModelCharacterisationTests.The_committed_model_snapshot_still_describes_the_model`
  (must be green at the end; it goes red between the configuration and the migration commits only if they
  are split — keep them in one commit).

### 5.3 New tests (red first unless marked)

In-process, `AuditReadEndpointAdversarialTests` (Architecture.Tests):

- **N1** `A_caller_with_no_usable_fab_group_searches_only_the_not_applicable_row` — theory over
  `UnscopedGroupClaims`; `GET /audit?pageSize=200` → 200, contains `N`, not `U`/`M`/`B`. **Red** (today
  contains `U`).
- **N2** `A_caller_with_no_usable_fab_group_is_answered_for_a_resolved_row_byte_for_byte_what_an_unknown_identifier_answers`
  — theory over `UnscopedGroupClaims` × {`M`, `B`}; 404 equal to unknown; body contains neither fab. **Red**
  (today throws `FabAuthorizationException`).
- **N3** `A_not_applicable_row_is_returned_to_a_caller_with_no_usable_fab_group` — theory over
  `UnscopedGroupClaims`; 200 with `N`. **Characterisation, green before and after** (guards over-cure).
- **N4** `A_caller_holding_both_fabs_reads_either_resolved_row_by_identifier` —
  `/fabs/munich /fabs/berlin` → `M` and `B` both 200. **Characterisation** (guards that item 2 does not
  narrow a multi-fab caller).

In-process, `GetAuditEventScopeTests` (Application.Tests):

- **N5** `A_resolved_row_is_not_found_for_a_caller_with_no_usable_fab_claim` — `[]` and `["Munich"]`, a
  `munich` row → error equals unknown. **Red** (today `IsSuccess`).
- **N6** `A_resolved_row_of_the_callers_own_fab_is_returned` — `["munich"]`, `munich` row → success.
  **Characterisation.**

CI-only, Integration.Tests:

- **N7** `The_not_applicable_partial_index_exists_on_the_hypertable` — add to
  `NeutralFabRetentionRowIntegrationTests` (already in `ci-shards/shard-4.filter`, so no shard entry is
  needed; a new class would need one). Query `pg_indexes` for `tablename = 'audit_events' AND indexname =
  'ix_audit_not_applicable_occurred'` (pattern: `EventTypeRegistryConcurrencyIntegrationTests:56`), via
  `aspire.CreateAuditObservabilityDbContextAsync()`; assert one row whose `indexdef` contains `(occurred_at)`
  and `fab_attribution` and `'NotApplicable'`. Cannot be observed red locally (Docker down); its red is
  carried by R16 in-process. If a counterfactual is wanted, spec 417's draft-PR method applies.

## 6. Commit structure — one PR

One PR, branch `fix/3035-unscoped-audit-reads`, `--base develop`. Items 1 and 2 share
`AuditReadScope.cs`, `GetAuditEventQueryHandler.cs`, `AuditEndpoints.cs` and four test files, so splitting
them would only serialise the same edits across PRs. Item 3 is file-disjoint and *could* be its own PR, but
the owner scoped one issue, there is one branch, and it needs no reviewer the other two do not already get.

Rebase-merge lands each commit (ADR-0087); every commit must build on its own. Red commits leave their own
suite failing until the next commit (accepted, CLAUDE.md).

1. `docs(3035): specify, plan and task unscoped audit reads and the by-id 404 (spec 422)` — this commit.
2. `test(3035): an audit read with no usable fab claim sees only fab-neutral rows (red)` — R1-R9, N1, N3 (N3
   green).
3. `fix(3035): an audit read with no usable fab claim sees only fab-neutral rows` — `AuditReadScope` item-1
   edit + doc comments.
4. `test(3035): a by-id audit read answers 404 for every row the caller may not read (red)` — R10-R15, N2,
   N4-N6. R15 (route table) is red until commit 5 removes the parameter.
5. `fix(3035): a by-id audit read answers 404 for every row the caller may not read` — `Admits`, handler,
   endpoint.
6. `test(3035): the audit model carries the not-applicable partial index (red)` — R16, N7.
7. `perf(3035): index the fab-neutral audit rows by occurred_at` — configuration + migration + snapshot in
   one commit (so `HasPendingModelChanges` is never red at a commit boundary).
8. `docs(3035): tick the completed tasks of spec 422`.

## 7. Risks

- **Compressed chunks** (spec A1) — unverified; block rather than decompress.
- **EF churn in the generated migration** — remove by hand; reviewer checks `git diff --stat` on
  `Migrations/` (SC-4).
- **A user who relied on the unscoped view.** In the seeded realm every human principal holds a usable fab
  (spec 396 A1; `UnresolvedFabAuditRowIntegrationTests:374`). A real deployment's sub-group-only accounts
  (`/fabs/munich/ops`) lose their view of `Unresolved` rows — the owner's intended effect.
- **Shard filter** — N7 goes into an existing shard-4 class; a new integration class would fail
  deterministically without a `shard-N.filter` entry.
- **Release analyzers** — removing a parameter must not leave an unused `using`
  (`IDE0005`); `ServiceDefaults.Authorization` is still used by `IFabAuthorizationGuard` in `Search` /
  `GetTimeline`.

## 8. Agents

| Phase | Agent |
|---|---|
| 4a | `test-writer` (commits 2, 4, 6) — returns verbatim red output |
| 4b | `backend-engineer` (commits 3, 5, 7) — may not edit the tests |
| 6 | `backend-reviewer`, **`security-reviewer`**, `/security-review` |
