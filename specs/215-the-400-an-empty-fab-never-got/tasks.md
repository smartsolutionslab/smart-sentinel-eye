# Tasks 215 — The 400 an empty fab never got

**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2507](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2507)
**Phase:** 3 — ready for a backend-engineer
**Phase 4a colour:** **RED** (behaviour-changing — 500 → 400)

---

## Parallelism, stated once

**There is almost none here, and that is the correct shape.** Every production
task edits the same file (`src/AuditObservability/Api/AuditEndpoints.cs`) and
every test task edits the same file
(`tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`).
ADR-0109 marks `[P]` only for **disjoint files**, so marking these would be a
lie the orchestrator would act on.

The one genuine `[P]` pair is **T001 ∥ T002** — two different reads, no writes.

**Do not fan this out.** One backend-engineer, start to finish. The whole change
is roughly fifteen production lines; a second agent would cost more in merge
conflict than it saves.

---

## Dependency order

```
T001 [P] ─┐
T002 [P] ─┴─> T003 ─> T004 (RED gate) ─> T005 ─> T006 ─> T007 ─> T008 ─> T009
```

T004 is a **gate**: the verbatim red output is the artifact phase 4b consumes and
the PR body quotes. Do not start T005 without it.

---

## US1 (P1) — an empty required fab gets a client error

### T001 [P] [US1] — confirm the binding premise (A-1) before writing anything

**Files read:** none written.
Run the stack (or `dotnet test` against the Aspire fixture) and issue
`GET /audit/overlay/{any-guid}?fabId=` as `admin@munich.test`.

**Done when:** the observed status is recorded. **If it is 500**, A-1 holds and
work continues. **If it is 400**, the premise is wrong — stop, comment on #2507
with the observed response, and hand back to phase 1. Do not "fix" a defect the
system does not have.

*This exists because spec 214's own premise check found an issue's claim stale,
and because several board issues this session were wrong on inspection.*

### T002 [P] [US1] — read `SearchAuditQueryHandler`'s fab predicate

**File read:** `src/AuditObservability/Application/Queries/Handlers/SearchAuditQueryHandler.cs`

**Done when:** you can state whether `SearchAuditQuery.Fab == ""` is treated as
"no fab filter" (same as `null`) or as an equality filter on the empty string.

**Feeds T006.** If it filters, T006 must also normalise `Fab` to `null`; if it
does not, T006 is the one-token predicate change and nothing more. Recorded in
the plan as the one thing not to assume.

### T003 [US1] — read the file being changed

**File read:** `src/AuditObservability/Api/AuditEndpoints.cs` (lines 60–190) and
`tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
in full.

**Done when:** you can name the two existing assertions that must survive
unmodified (SC-3, SC-4) and the seed helpers the new tests will reuse
(`SeedAsync`, `OverlayRow`). Mirror them; do not introduce a new seeding style.

---

### T004 [US1] [US2] — **RED GATE**: write the failing tests, observe them fail, quote the output

**Agent:** `test-writer` (phase 4a). **Writes tests only. Does not touch `src/`.**

**File:** `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
(extend — do not create a new file; the plan says why).

Add, in this order:

| Test | Scenario | Expected after fix | Colour now |
|---|---|---|---|
| `An_empty_fab_on_a_resource_timeline_is_a_client_error` | SC-1 · `?fabId=` | 400 `AUDIT_INVALID_INPUT` | **RED** |
| `A_whitespace_fab_on_a_resource_timeline_is_a_client_error` | SC-2 · `?fabId=%20` | 400 `AUDIT_INVALID_INPUT` | **RED** |
| `An_empty_fab_on_the_audit_search_spans_the_callers_fabs` | SC-8 · `GET /audit?fabId=` | 200, rows scoped to caller | **RED** |
| `A_malformed_fab_grammar_keeps_its_existing_client_error` | SC-6 · `?fabId=NOT_A_FAB` | 400 `AUDIT_INVALID_INPUT` | **RED — reclassified** (was labelled characterisation; re-derived from source and found to answer 403 today, not 400 — see spec.md's correction) |
| `A_cross_fab_timeline_is_refused_before_a_malformed_resource_is_parsed` | SC-5 | 403 `RESOURCE_FAB_NOT_AUTHORIZED` | green (characterisation) |
| `An_omitted_fab_keeps_the_frameworks_own_refusal` | SC-7 | 400 | green (characterisation) |
| `A_cross_fab_audit_search_is_still_refused` | SC-9 · `GET /audit?fabId=berlin` | 403 `RESOURCE_FAB_NOT_AUTHORIZED` | green (characterisation) |
| `An_unauthenticated_empty_fab_request_is_challenged` | SC-10 | 401 | green (characterisation) |

Assert **both** the status **and** the problem `title` on every non-200 — a
status-only assertion cannot tell `AUDIT_INVALID_INPUT` from a framework 400 and
would pass on a fix that produced the right number for the wrong reason.

**Done when:** all four RED tests (SC-1, SC-2, SC-8, and SC-6) have been run and
**observed failing**, and the **verbatim** failure output (including the actual
status each one currently produces — SC-6 currently answers 403, not 500, so its
failure message will look different from the other three) is returned to the
orchestrator. A test that arrives green is a phase-4 failure, not a shortcut
(ADR-0144). The four remaining characterisation tests must be observed **green**
in the same run — they are the before-picture for SC-D.

**Do not proceed to T005 without this output.**

---

### T005 [US1] — reorder `GetTimeline` into three gates

**Agent:** `backend-engineer` (phase 4b). **Brief:** T004's verbatim output.
**May not edit any test to make it pass.**

**File:** `src/AuditObservability/Api/AuditEndpoints.cs`, `GetTimeline` (lines 100–147).

Split the single `try` at line 123 into two, with the guard between them:

1. parse `FabIdentifier.From(fabId)` → on `ArgumentException`, return the
   existing `AUDIT_INVALID_INPUT` 400 (same `catch` body as today);
2. `await fabGuard.EnsureAccessAsync(user, parsedFab.Value, cancellationToken);`
   — note `.Value`, matching `DevicesEndpoints.cs:134`;
3. parse `ResourceIdentifier.From(resourceIdentifier)` → same 400.

**Constraints, each of which a reviewer will check:**
- **No new error code.** Reuse `AUDIT_INVALID_INPUT` verbatim (SC-E).
- **Do not move step 3 ahead of step 2** — SC-5 fails if you do, and the fab
  boundary moves.
- **Do not touch** `src/ServiceDefaults/Authorization/IFabAuthorizationGuard.cs`
  (SC-F).
- Extend the existing comment at lines 115–120 with the *why* of the new ordering
  (one clause). Do not narrate the sequence.

**Done when:** SC-1, SC-2, and SC-6 are all green (SC-6 moving from 403 to 400 is
the same reordering fixing a second, previously-unnoticed instance of the
defect — no separate production change is needed for it) and every
characterisation test is still green, unmodified.

### T006 [US2] — widen `Search`'s fab predicate

**File:** `src/AuditObservability/Api/AuditEndpoints.cs`, `Search` (line 76).

`if (fabId is not null)` → `if (!string.IsNullOrWhiteSpace(fabId))`.

Apply T002's finding: if `SearchAuditQueryHandler` treats `Fab: ""` as an equality
filter, also pass `Fab: string.IsNullOrWhiteSpace(fabId) ? null : fabId`.

**Done when:** SC-8 is green and SC-9 (the cross-fab 403) is still green. SC-9 is
the one that catches "skipped the guard" being mistaken for "widened the
predicate".

### T007 [US1] [US2] — prove no code was left behind

Run, and record the output:

- `dotnet build -c Release` — analyzers and `TreatWarningsAsErrors` clean.
- `grep -rn "AUDIT_" --include=*.cs src/AuditObservability` — confirm the set of
  error codes is **unchanged** from `origin/develop` (SC-E).
- `git diff origin/develop --stat` — confirm `src/` touches exactly one file
  (SC-F, and the plan's "one file" claim).

**Done when:** all three are recorded in the PR body. A claim of "one file
changed" that nobody ran `--stat` for is the kind of unchecked record this
repository has had to correct repeatedly.

---

## Phase 5 — T008 [US1] [US2] — verify end to end

**Agent:** `/verify`.

Run the **five-probe table** in spec §"Independent end-to-end test procedure"
against a booted Aspire stack, by hand over HTTP — not by re-running the tests.

Also check probe 6: the `audit-observability` structured logs carry **no unhandled
exception** for the `?fabId=` probes. A status code can be right while an
exception is still being thrown and caught somewhere unintended; the log is what
distinguishes a fix from a rewrite of the symptom.

**Before trusting a manual probe:** confirm the running AppHost's process start
time is *after* the commit under test. A persistent stack serves whatever binaries
it booted with.

**Done when:** `verification.md` exists in this spec directory with the five
observed statuses, the five observed titles, and the log check. Write every result
down — a measurement reported only to the orchestrator is invisible to the next
reader.

**Latency:** cite **N/A**, not on any §IV leg. State it explicitly rather than
omitting it.

## Phase 6 — T009 [US1] [US2] — review

`/code-review`. `/security-review` is **not** required: the change adds no
authorization path and removes none, and a 500 → 400 on this input discloses
nothing a 400 does not. Record that judgement in the PR rather than skipping the
row silently.

The reviewer should specifically check **SC-D**: that no pre-existing assertion in
`CrossFabReadGuardIntegrationTests.cs` was edited. An edited assertion is evidence
the behaviour moved and blocks the work.

---

## Board gate (phase 3)

Feature-level issue only — **no per-task issues** (per CLAUDE.md, the practice
since spec 028). #2507 is the feature issue, and it is **already on Project #13**
— verified at phase 3 by `content.number`, not assumed. The gate is satisfied;
what follows is the re-check command, not an outstanding action.

```sh
gh project item-list 13 --owner smartsolutionslab --limit 2000 --format json \
  | grep 2507
```

`item-list` defaults to 30 items, so the `--limit 2000` is load-bearing. If #2507
is absent:

```sh
gh project item-add 13 --owner smartsolutionslab \
  --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2507
```

`item-add` prints nothing on success. Needs the `project` scope.

---

## Definition of done

- [ ] SC-1 … SC-10 all green.
- [ ] The three RED tests were **observed failing first**, output quoted verbatim
      in the PR body (ADR-0139, ADR-0144).
- [ ] SC-D: no existing assertion edited.
- [ ] SC-E: no new error code.
- [ ] SC-F: `IFabAuthorizationGuard.cs` untouched.
- [ ] `verification.md` written, with the five observed probes and the log check.
- [ ] `git diff origin/develop --stat` shows one `src/` file.
- [ ] PR opened against **`develop`** (`gh pr create --base develop`), referencing
      T005/T006 and closing #2507 with a closing keyword — then check the issue
      state after the merge; a mention alone auto-closes about one time in three.
