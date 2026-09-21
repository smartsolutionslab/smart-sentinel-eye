# Tasks — Spec 209, the fab a template never had

**Spec:** `specs/209-the-fab-a-template-never-had/spec.md` ·
**Plan:** `specs/209-the-fab-a-template-never-had/plan.md` ·
**Issue:** #2506 · **Branch:** `2506-overlay-published-null-fab` · **Base:** `origin/develop` @ `1e461ea2`

**Phase-4a colour: RED, for the whole spec** (constitution §Testing, ADR-0139, CLAUDE.md §*Phase 4a has two colours*). Not ambiguous, so the ambiguity rule does not need to resolve it: the fix changes what a query returns. Nothing here is behaviour-preserving, so there is **no characterisation half** and no test is captured green before a change.

`test-writer` writes the tests, runs them, and returns the **verbatim** output. That output is the engineer's brief and is quoted in the PR body. **The engineer may not edit the tests to pass.**

**One honest caveat on the red, to be stated in the phase-4a report:** T001 adds three unit cases, of which **one fails and two pass**. The two that pass are the widening-not-removal guards — they exist to fail if the fix over-reaches, so a green start is their correct state. The story's red is carried by `A_row_with_no_fab_is_returned_by_a_fab_scoped_timeline` and by T002's integration case, and those two failures are the evidence. A reader must not mistake a partially-green run for a shortcut, which is why it is written down here rather than explained afterwards.

**No new ADR, no constitution amendment, no task issues.** The phase-3 gate is *the feature's issue on Project #13* (CLAUDE.md §Workflow) — #2506, added at dispatch. `/speckit-taskstoissues` is **not** to be run: per-task issues stopped at spec 028, and this spec would add nine items to a board used at feature granularity.

---

## Ordering

The fix is one line. The order exists entirely to satisfy the phase-4 gate.

```
T001 [P] unit RED  ┐
                   ├─→ T003 (the fix)  →  T004 (the comment)  →  T005 (verify)  →  T006/T007 (QA)  →  T008/T009 (PR)
T002 [P] intg RED  ┘
```

T001 and T002 must both be **observed failing before T003 exists**. Writing the fix first leaves the story with no red to quote and the phase-4 gate unmet — there is no way to reconstruct the failure afterwards, because the fix makes it unreproducible.

## Parallelism (ADR-0109)

| Group | Tasks | Why |
|---|---|---|
| **A** | **T001 `[P]`, T002 `[P]`** | Genuinely disjoint files, one unit and one integration, neither reading the other. The two red tasks are the whole parallel opportunity and they are the tasks that gate everything else, so the fan-out is worth taking. |
| — | T003, T004 | **Serialised.** Both own `GetResourceTimelineQueryHandler.cs`, and T004's comment lands directly above T003's predicate. Never `[P]` against each other. |
| — | T005 → T009 | Sequential by definition (verify, then QA, then PR). |

**No foundational blocker.** Nothing in `Shared.Kernel`, `Shared.Contracts`, `AppHost` or any Aspire resource changes; no `.csproj` is edited; no migration is generated. There is no gate the rest of the work waits behind other than red-before-green.

**This spec has almost no parallelism and that is the honest answer, not a failure to decompose.** One production file carries the entire change. Marking more tasks `[P]` would hand the orchestrator a fan-out that produces merge conflicts inside a three-line diff.

**If the slice has to narrow:** it cannot, and should not. T001–T004 is already the minimum that closes #2506 with evidence. Do **not** ship T003 without T001/T002 — that is a fix with no red, which the phase-4 gate rejects.

---

## US1 (P1) — A fab-neutral audit row is reachable from a fab-scoped timeline

### Phase 4a — RED (`test-writer`)

**[T001] [P] [US1] Unit: three cases on the timeline handler's fab predicate.**
File: `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineQueryHandlerTests.cs` (existing, extended).

- Give the private `Row(...)` helper (`:27-32`) a `string? fab = "munich"` parameter and pass it to `.WithFab(fab)`. **`AuditEventBuilder.WithFab` already accepts `string?`** and already maps null to `Option<FabIdentifier>.None` (`AuditEventBuilder.cs:25`, `:39-41`) — the builder needs no change.
- `A_row_with_no_fab_is_returned_by_a_fab_scoped_timeline` — seed one row with `fab: null`; query `Q()` (kind `overlay`, fab `munich`); expect one row back. **Fails today with an empty page. This is the filed defect and the primary red.**
- `A_row_belonging_to_another_fab_is_still_excluded` — seed a `berlin` row and a `null` row; expect exactly the `null` row. **Passes today; must still pass.** This is the guard that a deleted predicate breaks.
- `A_fab_neutral_row_for_a_different_resource_is_still_excluded` — seed a `fab: null` row with kind `camera`; expect an empty page from the `overlay` query. **Passes today; must still pass.**
- Sentence-style names (ADR-0053), Shouldly assertions (ADR-0052), no AutoFixture (ADR-0054).
- **Each assertion must be able to fail for a reason other than its own input** (CLAUDE.md house lesson): assert on the returned page's row count and the returned row's fab, never restate the seed.

Run, capture the verbatim output, report it. Do **not** write any production code.

**[T002] [P] [US1] Integration: the same defect through the live endpoint.**
File: `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` (existing, extended). Aspire fixture (ADR-0103); no Testcontainers.

- `A_fab_neutral_row_is_returned_by_a_fab_scoped_resource_timeline`: pick a fresh `Guid.CreateVersion7()` overlay identifier; `SeedAsync` a row on `("overlay", that identifier)` with `fab: null` **and** one with `fab: "berlin"`; request `GET /audit/overlay/{id}?fabId=munich` as `admin@munich.test`; assert **200**, the fab-neutral row **present**, and the `berlin` row **absent**.
- Both halves in the one test. A presence-only assertion passes on a deleted predicate, which is the mistake this test exists to make impossible.
- **This test, not T001, is what proves the LINQ translates.** A unit fake queries a `List<T>` in memory and would happily evaluate a predicate Postgres cannot.
- **Do not modify** `Munich_member_reads_its_own_fab_timeline_but_is_refused_another_fab` (`:21-35`). If it starts failing at any point in this spec, the security boundary has moved: **block, do not adjust.**

Run, capture the verbatim output, report it. Do **not** write any production code.

### Phase 4b — GREEN (`backend-engineer`)

**[T003] [US1] Widen the fab predicate.**
File: `src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs`, the third `Where` at `:54`.

```csharp
.Where(auditEvent => auditEvent.Fab == null || auditEvent.Fab == fabFilter);
```

- Compare the **value object**, not `.Value` — EF Core cannot translate member access on a value-converted CLR type, and the file already carries that comment at `:48-49`.
- Leave `FabIdentifier fabFilter = fab;` (`:51`) exactly as it is.
- Leave the two resource predicates, the page-size checks, the cursor decode, the ordering and the DTO mapping untouched.
- Leave the endpoint untouched: `fabId` stays required, `fabGuard.EnsureAccessAsync` (`AuditEndpoints.cs:114`) stays where it is.
- **No migration, no index change.** `ix_audit_resource_occurred` carries no fab column (`AuditEventConfiguration.cs:148-149`), so the plan does not move.
- **Do not touch `src/OverlayDesigner/`.** Its `Fab: null` is ADR-0115's decision, not a defect. If the diff shows a file under `src/OverlayDesigner/`, the fix is wrong.

Depends on: T001 + T002 both observed red.

**[T004] [US2] Attach the reason to the predicate.**
Same file, immediately above the widened `Where`. Modelled on `SearchAuditQueryHandler.cs:63-72`. It must name:

- **#1300** — the issue that decided this rule for the search path;
- **ADR-0115** — why overlay lifecycle events legitimately carry no fab;
- the other two fab-neutral producer classes — **retention** (spans fabs) and **unattributable stream health** (#2076);
- the mechanism of the bug: `fabId` is **required** here, so fab equality made the whole class readable by nobody.

Says *why*, never *what* (CLAUDE.md §*No drive-by comments*). No issue reference goes anywhere but this comment and the PR body.

Depends on: T003. **Serialised with it** — same edit region.

---

## Phase 5 — Verify (`/verify`)

**[T005] [US1] Observe it end to end against the running stack, and write the note.**

Follow `spec.md` §*Independent end-to-end test procedure* exactly. The non-negotiable parts:

- **One Aspire stack per machine.** A second concurrent boot produces `FailedToStart` that reads exactly like a code defect.
- **Mint the token from Aspire's proxied Keycloak endpoint**, not the container's mapped port, or everything 401s.
- **Run the control first** (step 5): `GET /audit?resourceKind=overlay&resourceIdentifier={O}` must show the row with `fab: null`. Without it, an empty step 6 is indistinguishable from an overlay that never published.
- **Quote both halves of step 6** — the empty page on `origin/develop` and the populated page on the branch. The contrast *is* the evidence; the populated page alone is not.
- Run steps 7 (403 on `?fabId=berlin`) and 8 (400 with no `fabId`) and record both. They are what stop a green step 6 from being an accident.
- **Check the running host's PID start time against the branch commit** before trusting a manual `curl` — a persistent AppHost serves whatever binaries were loaded at boot (CLAUDE.md house lesson).
- **Write every figure and response down in the verification note as observed.** A result reported only to the orchestrator is invisible to every later grep and reviewer.

Artifact: `specs/209-the-fab-a-template-never-had/verification.md`.

**Latency:** cite **N/A** explicitly, with the reason from `spec.md` §*Latency-budget impact* — the audit read path is on no leg of constitution §IV. Do not cite a figure; a leg recorded as measured before anyone read its figure claims a discharge nobody earned.

Depends on: T004.

---

## Phase 6 — QA

**[T006] [US1] `/code-review`.**
Use `plan.md` §*Review focus for phase 6* as the checklist. In particular: the predicate is **widened, not removed**; the 403 test is **unmodified**; the comparison is on the value object; nothing under `src/OverlayDesigner/` is touched.

**[T007] [US1] `/security-review`.**
**Required, not optional.** The change widens what a fab-scoped read returns. The reviewer should *check* the disclosure argument rather than accept it: `GetSingle` (`AuditEndpoints.cs:181`) already returns null-fab rows to any `sse.audit.read` holder with no fab check, and `SearchAuditQueryHandler.cs:73` already includes them for fab-assigned callers — so the timeline is the last of three read paths to stop excluding them, and no row becomes reachable that was not already reachable by two other routes. Confirm the `403` path for a fab the caller does not hold is intact.

All findings resolved, or accepted in writing.

Depends on: T005.

---

## Phase 7 — PR

**[T008] [US1] Commits.**
Conventional Commits (ADR-0030). **No `Co-Authored-By` footer** — ADR-0086 is absolute in this repo and beats any session attribution reminder. Each commit must build **on its own**: rebase-merge lands them individually on `develop` (ADR-0087), so a commit that only compiles at the tip of the branch breaks `git bisect` for ever.

Suggested split:

1. `test(audit): prove a fab-neutral row is unreachable from a fab-scoped timeline` — T001 + T002, red.
2. `fix(audit): include fab-neutral rows in a fab-scoped resource timeline` — T003 + T004.

**[T009] [US1] Open the PR.**
`gh pr create --base develop` (ADR-0028 — never against `main`, and pass the flag explicitly as insurance). PR body must carry:

- the **verbatim** red output from T001 and T002, including the note that two of the three unit cases start green and why;
- a closing keyword for **#2506** — and check the issue's state after the merge, because a mention alone closes it about one time in three;
- the T005 verification note's step-6 contrast;
- **the correction to the issue's premise, prominently.** The issue proposes fixing `OverlayDesigner`; ADR-0115 forbids that, and the actual fix is in `AuditObservability`. A reviewer arriving from the issue title will otherwise look for a diff that is not there.
- `Phase 5: …` / `Phase 6: …` lines as delivered. No phase is skipped.

Then **park** — do not wait for CI (CLAUDE.md §*The autonomous lane*). Start the watcher, take the next issue, rebase this branch after every merge that lands on `develop`.

---

## Task table

| ID | `[P]` | Story | Owner | File(s) | Depends on |
|---|---|---|---|---|---|
| T001 | `[P]` | US1 | `test-writer` | `tests/AuditObservability.Application.Tests/Queries/Handlers/GetResourceTimelineQueryHandlerTests.cs` | — |
| T002 | `[P]` | US1 | `test-writer` | `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs` | — |
| T003 | — | US1 | `backend-engineer` | `src/AuditObservability/Application/Queries/Handlers/GetResourceTimelineQueryHandler.cs` | T001, T002 red |
| T004 | — | US2 | `backend-engineer` | same file | T003 |
| T005 | — | US1 | `/verify` | `specs/209-the-fab-a-template-never-had/verification.md` | T004 |
| T006 | — | US1 | `/code-review` | — | T005 |
| T007 | — | US1 | `/security-review` | — | T005 |
| T008 | — | US1 | orchestrator | — | T006, T007 |
| T009 | — | US1 | orchestrator | — | T008 |
