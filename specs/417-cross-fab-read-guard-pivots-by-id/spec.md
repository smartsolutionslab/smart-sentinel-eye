# Spec 417: The cross-fab read-guard tests find their seeded fab-neutral row by identifier

**Feature Branch**: `test/3035-pivot-null-fab-assertions` (cut from `origin/develop` at `d24ee84e`)

**Created**: 2026-10-19

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` is left in the lane scope.

**Input**: Issue [#3035](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3035), body and both owner
comments. The enrolment comment limits the lane to fixes that need no product or authorization-model decision; the
pick-up comment narrows it to **item 4 only**.

**Lane.** Autonomous (ADR-0144). One PR, test-only, no production change.

**Spec number.** 417. Checked 2026-10-19: `origin/develop` holds up to 415; 416 is claimed by the unmerged PR #3060
(`refactor/3023-fold-bracket-scan`, `specs/416-audit-bracket-scan-folds-onto-url-authority/`); the only other open PR
(#2811) carries no spec. **Check again before the PR is opened.**

**ADRs.** ADR-0103 (integration tests run against the real Aspire stack, no Testcontainers), ADR-0139 / ADR-0144
(phase 4a colours; a gate is never weakened; the lane implements, it does not decide), ADR-0053 (test naming,
unchanged), ADR-0159 §2 (the fab scope the tests exercise, unchanged), ADR-0036 (smallest change). Prior art: spec 396
(#2927), which introduced `FabAttribution.Unresolved` hiding and already pivots its two newer tests by identifier
(`A_named_fab_search_returns_a_not_applicable_row`, `A_seeded_unresolved_row_is_hidden_from_every_fab_scoped_path_...`).

**Latency budget.** N/A. No code on the event-to-overlay path changes; no production code changes at all.

---

## 1. Premise check (done before writing anything)

| #3035 item | Verdict on `develop` (`d24ee84e`) | In this spec? |
|---|---|---|
| 1. Caller with no usable `/fabs/*` claim reads every fab's `Unresolved` rows | **Still true.** `AuditReadScope.Apply` falls through to `Fab == null` when `IsScoped` is false (`src/AuditObservability/Application/Queries/Handlers/AuditReadScope.cs:52-56`). Either cure changes who may see what and reverses spec 245's pin. | **No.** Owner decision. |
| 2. `GET /audit/{id}` 403 vs 404 existence oracle | **Still true.** Pinned by this very file (`CrossFabReadGuardIntegrationTests.cs:239-240`, "the Resolved-row 403 is unchanged"). Changing it changes what a caller learns. | **No.** Owner decision. |
| 3. No index serves `fab_attribution = 'NotApplicable'` | **Still true.** `AuditEventConfiguration.cs:162-178` has no partial index on `fab_attribution`. Needs an `EXPLAIN (ANALYZE, BUFFERS)` on a live seeded database and a hypertable migration. | **No.** Needs a running stack and a measurement. |
| 4. `ShouldContain((string?)null)` over a shared, never-reset table | **Still true**, at `CrossFabReadGuardIntegrationTests.cs:110` and `:164` (the issue's ~:97 / ~:138 are the tests' first lines). | **Yes.** |

Item 4 is decision-free: it changes no production behaviour and no rule about who may see what. It only makes two
existing assertions specific to the row each test seeds.

## 2. The defect

Two tests seed one fab-neutral (`FabScope.Neutral` -> `FabAttribution.NotApplicable`) row through `NeutralRow()`,
then query `/audit?pageSize=200` with no resource pivot and assert that **some** returned row has `fab == null`.
The `audit_events` table is shared across the whole `AspireCollection` and never reset. So the assertion:

- passes when the seeded row is **missing**, provided any other null-fab row from any earlier test (spec 396's own
  seeds, `NeutralFabRetentionRowIntegrationTests`, real overlay events) is in the newest 200;
- passes when a regression returns `Unresolved` rows to a fab-scoped caller **instead of** `NotApplicable` ones,
  because on the wire both carry `"fab": null`;
- therefore cannot fail for the reason its message states ("a cross-fab row must be readable by a fab-assigned
  caller").

The universal negative `fabs.ShouldNotContain("berlin")` at `:109` is **not** affected: a universal assertion over a
shared table is valid (and stronger than one over the seeded row alone), so it stays exactly as it is.

## 3. User story

### US1 (P1): a maintainer can trust that the two #1300 assertions fail when the seeded fab-neutral row stops being readable

**Why this priority**: it is the only story. It restores a guard that currently passes on unrelated data.

**Independent test**: CI's integration job runs both tests green against unchanged production code, and the plan §5
counterfactual (the seeded row made `Unresolved`) turns both red while the old assertion text would not have.

**Acceptance scenarios** (Gherkin; production behaviour is unchanged, so these describe the strengthened tests):

```gherkin
Scenario: Happy path - the seeded NotApplicable row is found by its own identifier (unnamed search)
  Given a berlin Resolved row and a NotApplicable row R are seeded
  And admin@munich.test (member of /fabs/munich) is authenticated
  When it calls GET /audit?pageSize=200
  Then the response is 200
  And a row with auditIdentifier = R.Id is present
  And that row's fab is null
  And no row has fab "berlin"

Scenario: Happy path - the seeded NotApplicable row reaches a berlin-only operator
  Given a NotApplicable row R is seeded
  And op-berlin@berlin.test is authenticated
  When it calls GET /audit?pageSize=200
  Then the response is 200
  And a row with auditIdentifier = R.Id is present
  And that row's fab is null

Scenario: Conflict - the seeded row is Unresolved instead (counterfactual, not committed)
  Given the seeded row R is FabScope.Owned with no fab (FabAttribution.Unresolved)
  When either test runs
  Then it fails on "a row with auditIdentifier = R.Id is present"
  # The old ShouldContain(null) assertion would still pass on any other null-fab row in the table.

Scenario: Bad request / auth - unchanged and not re-specified here
  # Bad-request (empty, whitespace, malformed fab) and auth (401, cross-fab 403) cases already exist in the same
  # class and are not touched.
```

## 4. Functional requirements

- **FR-001** At `:110`, replace "some row has a null fab" with: the row whose `auditIdentifier` equals the seeded
  `NeutralRow()`'s `Id` is present **and** its `fab` is null. Keep `:109` (`ShouldNotContain("berlin")`) byte-for-byte.
- **FR-002** At `:162-164`, the same replacement for the row seeded at `:138`, read by the berlin-only operator.
- **FR-003** Each new assertion must **logically imply** the one it replaces (plan §4). Nothing is deleted without a
  stronger assertion in its place; no message is reworded to say less.
- **FR-004** No production file, fixture, seed helper signature, shard filter or other test changes. The test names
  stay (ADR-0053; a rename would also orphan CI history).
- **FR-005** The `<summary>` of the first test, which claims the test seeds rather than "asserting over whatever the
  run happens to have produced", is made true by the change; it is edited only if a sentence becomes false.

## 5. Success criteria

- **SC-1** `dotnet build` of `tests/Integration.Tests` succeeds with zero new warnings (Release analyzers).
- **SC-2** CI's integration job on the PR tip reports both test names **Passed** in its trx (not absent, not skipped).
- **SC-3** The counterfactual of plan §5 is observed red for both tests, failure quoted verbatim in the PR.
- **SC-4** `git diff --stat origin/develop` lists only this spec directory and
  `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`.

## 6. Out of scope (reported, not decided)

- #3035 items 1, 2, 3 (see §1).
- Adjacent, same family, **not** named by item 4: `:317` `fabs.ShouldContain("munich")` in
  `An_empty_fab_on_the_audit_search_spans_the_callers_fabs` scans the shared table for a positive in the same way and
  would pass if its seeded munich row were missing. It is a different assertion (a fab, not the null/Unresolved
  ambiguity). Reported to the orchestrator as a follow-up candidate rather than widened into this PR.
- `:69` already pivots on a unique overlay identifier (one null-fab row seeded on it), so it is not ambiguous.

## 7. Assumptions

- **[Assumption A-1]** The search orders by `occurred_at` descending (`SearchAuditQueryHandler.cs:52`) and the seeded
  row carries `UtcNow`, so it lands in the newest 200. In the second test about 20 s of polling separate the seed from
  the read; fewer than 200 audit rows arrive in that window on a serial collection. If CI shows otherwise, the fix is
  to seed immediately before the berlin read (plan §3), not to drop the assertion.
- **[Assumption A-2]** The search response row's `auditIdentifier` equals the seeded `AuditEvent.Id.Value` — already
  relied on by `AuditIdentifiersAsync` in the spec 396 tests of the same class.
