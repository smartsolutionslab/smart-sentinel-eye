# Tasks 245 — The search that guards before it parses

**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2530](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2530)
**Phase:** 3 — ready for a backend-engineer
**Phase 4a colour:** **RED** (behaviour-changing — 403 → 400; throw → success)

---

## Parallelism

Two stories, two production files, two test files — genuinely disjoint (ADR-0109):

| Story | Production file | Test file |
|---|---|---|
| US1 | `src/AuditObservability/Api/AuditEndpoints.cs` | `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` |
| US2 | `src/AuditObservability/Application/Queries/Handlers/SearchAuditQueryHandler.cs` | `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditQueryHandlerTests.cs` |

So the `[P]` markers below are true. **Still, do not fan out.** The whole change
is roughly twenty production lines and one index; one backend-engineer, start to
finish, is cheaper than two branches. The markers are there so the order can be
chosen freely — US2's red is Docker-free and fastest; do it first if the Aspire
stack is busy.

No foundational task: nothing in `Shared.*`, `ServiceDefaults` or `AppHost` changes.

## Dependency order

```
T001 ─┬─> T002 [P][US1] RED ─> T004 [US1] ─┐
      └─> T003 [P][US2] RED ─> T005 [US2] ─┴─> T006 ─> T007 (verify) ─> T008 (review)
```

T002 and T003 are **gates**: their verbatim red output is phase 4b's brief and the
PR body's evidence. No `src/` edit before both exist.

---

## Setup

### T001 — re-check the premise at the tip

Read `AuditEndpoints.Search` (lines 60–102) and `SearchAuditQueryHandler.cs`
lines 46–79 at the branch tip. **Done when** you can confirm the guard still runs
on the raw `fabId` string and line 72 still maps `callerFabs` through
`FabIdentifier.From` in one expression. If either has changed on `develop` since
`a54b11d0`, stop and hand back to phase 1.

---

## US1 (P1) — malformed `fabId` on the search is a 400

### T002 [P] [US1] — RED GATE: the failing HTTP test

**Agent:** `test-writer`. Tests only; no `src/`.
**File:** `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` (extend).

Add `A_malformed_fab_on_the_audit_search_is_a_client_error_not_an_authorization_refusal`
(SC-1): `admin@munich.test`, `GET /audit?fabId=NOT_A_FAB`, assert **400** and
`title == "AUDIT_INVALID_INPUT"`. Mirror
`A_malformed_fab_grammar_now_gets_a_client_error_not_an_authorization_refusal`
(the timeline twin) for shape; a short `<summary>` citing spec 245 SC-1.

Run it together with the existing search characterisation tests
(`A_cross_fab_audit_search_is_still_refused`,
`An_empty_fab_on_the_audit_search_spans_the_callers_fabs`,
`Search_without_a_fab_filter_returns_the_callers_fabs_and_cross_fab_rows`).

**Done when:** SC-1 is **observed failing with 403 `RESOURCE_FAB_NOT_AUTHORIZED`**
(not a 500, not a boot failure — a `FailedToStart` is not a red), the three
characterisation tests are observed **green** in the same run, and the verbatim
output is returned. **Needs the Aspire stack: confirm no other worktree holds it.**

### T004 [US1] — parse before the guard in `Search`

**Agent:** `backend-engineer`. Brief: T002's output. May not edit tests.
**File:** `src/AuditObservability/Api/AuditEndpoints.cs`, `Search`.

Per plan §US1: blank ⇒ omitted; else `BoundaryParse.TryParse(() => FabIdentifier.From(fabId), "AUDIT_INVALID_INPUT", …)`,
then `EnsureAccessAsync(user, parsedFab.Value, …)`, then pass `parsedFab.Value`
as `Fab`. Update the stale comment above the query construction. No new error
code; `IFabAuthorizationGuard.cs` and `SearchAuditQuery.cs` untouched.

**Done when:** SC-1 green; T002's characterisation tests still green, unmodified.

---

## US2 (P2) — a malformed claimed fab costs only itself

### T003 [P] [US2] — RED GATE: the failing handler tests

**Agent:** `test-writer`. Tests only; no `src/`.
**File:** `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditQueryHandlerTests.cs` (extend).

| Test | Seed | Query | Assert |
|---|---|---|---|
| `A_malformed_claimed_fab_costs_only_itself` (SC-6) | rows in `munich`, `berlin`, `null` | `DefaultQuery(fab: null, callerFabs: ["munich", "Munich"])` | success; row fabs `["munich", null]` ignoring order |
| `A_wholly_malformed_claim_set_sees_only_cross_fab_rows` (SC-7) | rows in `munich`, `null` | `DefaultQuery(fab: null, callerFabs: ["Munich"])` | success; exactly one row, fab `null`; **no** `munich` row |

Use `AuditEventBuilder().WithFab(...)` and `TestAuditEventQuerySource`, as
`A_caller_with_fabs_sees_cross_fab_rows_alongside_their_own` does. SC-7's "no
munich row" is what forbids a lowercasing fix — assert it explicitly, not only via
the count.

**Done when:** both are **observed failing with `ArgumentException` from
`FabIdentifier.From`** (the grammar message, from `SearchAuditQueryHandler` line
72), the rest of the file is observed green in the same run, and the verbatim
output is returned. Runs without Docker:
`dotnet test tests/AuditObservability.Application.Tests --filter FullyQualifiedName~SearchAuditQueryHandlerTests`.

### T005 [US2] — parse claimed fabs per entry

**Agent:** `backend-engineer`. Brief: T003's output. May not edit tests.
**File:** `src/AuditObservability/Application/Queries/Handlers/SearchAuditQueryHandler.cs`, line 72.

Per plan §US2: build `allowed` with a per-entry `try { FabIdentifier.From } catch (ArgumentException) { }`,
one-line *why* comment in the catch (mirror `SystemVariableEndpoints.ResolveReadFabsAsync`).
No normalising, no broader catch, no third branch, no logging, #1300 comment
untouched. Do **not** add a catch at line 54 — US1 makes it unreachable.

**Done when:** SC-6 and SC-7 green; every pre-existing test in the file green,
unmodified.

---

## Close-out

### T006 [US1] [US2] — prove nothing else moved

Record in the PR body:

- `dotnet build -c Release` — clean, `TreatWarningsAsErrors`. If `Search` trips a
  **new** S138, extract per plan; never suppress.
- `dotnet format --verify-no-changes` on the touched projects.
- `git diff origin/develop --stat -- src/` — exactly the two files above.
- `grep -rn '"AUDIT_' src/AuditObservability --include=*.cs` — code set unchanged.

### T007 [US1] [US2] — phase 5, verify

**Agent:** `/verify`. Over HTTP on the booted stack, the five-probe table in spec
§"Independent end-to-end test procedure" (process start time after the commit).
Then re-run the plan §Appendix harness against the fixed tree; expected
`400, 400, 200, 403, 200, 200, 200, 400`. Write both, verbatim, to
`specs/245-the-search-that-guards-before-it-parses/verification.md`.
**Latency:** state **N/A** (no §IV leg) explicitly.

### T008 [US1] [US2] — phase 6, review

`/code-review` **and** the `security-reviewer` agent — the change edits the
predicate that decides which fab's rows a caller sees (plan §Security). Reviewers
check specifically: SC-C (no pre-existing assertion edited), I-3 (no
normalisation), the catch is `ArgumentException` only, and the 403 for a
well-formed unheld fab survives.

---

## Board gate (phase 3)

Feature-level issue only, no per-task issues (CLAUDE.md, since spec 028). **#2530
is on Project #13** — checked at phase 3 via
`gh issue view 2530 --json projectItems` → `"Smart Sentinel Eye"`. Labels at
pickup: `bug`, `agent:ready`.

## Definition of done

- [ ] SC-1, SC-6, SC-7 observed **red first**; output quoted verbatim in the PR.
- [ ] SC-1, SC-6, SC-7 green; all characterisation unmodified and green.
- [ ] Two `src/` files changed; no new error code; guard, `FabClaims`,
      `SearchAuditQuery` unchanged.
- [ ] `verification.md` with probes, harness re-run, latency N/A.
- [ ] PR to **`develop`** (`gh pr create --base develop`) with `Closes #2530`;
      check the issue state after merge.
