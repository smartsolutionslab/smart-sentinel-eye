# Tasks — Spec 210, the clock a filter meant

**Spec:** `specs/210-the-clock-a-filter-meant/spec.md`
**Plan:** `specs/210-the-clock-a-filter-meant/plan.md`
**Issue:** #2431
**Branch:** `2431-audit-date-clock-mismatch` (worktree `D:/Github/sse-2431`)
**Base:** `origin/develop` @ `3263084c`
**Phase-4a colour:** **RED** (behaviour-changing).

Format: `[ID] [P?] [Story]` — `[P]` means the task owns files disjoint from every other `[P]` task at the same step (ADR-0109).

---

## Ordering

```
T001 (red tests, US1) ──► T002 (fix, US1) ──► T004 (verify) ──► T005 (review) ──► T006 (PR)
T003a (red test, US2) ─► T003b (label, US2) ──┘
```

**T001 must be complete and its failure output captured before T002 begins.** That is the phase-4a gate, not a preference (ADR-0139).

## Parallelism (ADR-0109)

**There is essentially none to exploit, and that is the correct answer for this spec.** T001 and T003a both write `AuditPage.test.tsx`; T002 and T003b both write `AuditPage.tsx`. Two agents on one file is a merge conflict, not parallelism.

The parallelism that *does* exist is **against other specs**: this spec touches three files, all under `apps/management-web/src/features/audit/`, plus its own `specs/` directory. **Nothing in `src/`, `apps/shared`, `Shared.Contracts`, `AppHost`, `deploy/` or `e2e/`.** Any concurrently-running backend spec is fully disjoint from it. There is no foundational task here that blocks anything else.

---

## US1 (P1) — A filter means the clock the operator can read

### Phase 4a — RED (`test-writer`)

**[T001] [US1] Extend `AuditPage.test.tsx` with the clock-mismatch cases, observe them red, report the output verbatim.**

File: `apps/management-web/src/features/audit/AuditPage.test.tsx` (extend; do not restructure the existing 5 tests).

Reuse the file's existing machinery unchanged: the `vi.mock` of `@smart-sentinel-eye/shared/api/audit.api` (`:10-16`), `searchMock`, `result(...)`, `renderPage()`, and the `expect(searchMock).toHaveBeenLastCalledWith(expect.objectContaining({...}))` assertion style (`:85`).

Write these cases, sentence-style names to match the file:

1. **`Applying a Since filter sends an ISO instant, not a bare local wall clock`** — the **zone-independent** case. Set *Since* to `2026-09-17T08:00`, Search, assert
   `expect.objectContaining({ since: new Date('2026-09-17T08:00').toISOString() })`.
   **This one must be red in UTC**, which is where CI runs (`ci.yml:136`). If it is green before T002, stop and report — the premise has changed.
2. **`A Since filter typed east of UTC is sent as the instant it means`** — `process.env.TZ = 'Europe/Berlin'` in `beforeEach`, restored in `afterEach`; `2026-09-17T08:00` → `since === '2026-09-17T06:00:00.000Z'`.
3. **`A Since filter typed west of UTC is sent as the instant it means`** — `America/New_York`; `2026-09-17T08:00` → `'2026-09-17T12:00:00.000Z'`.
4. **`Both bounds are converted`** — *Since* and *Until* set; neither issued value retains the bare form (assert both end in `Z`).
5. **`Clearing the filters sends no date bounds`** — apply, then click **Clear**; the last call has neither a `since` nor an `until` key, and nothing throws.
6. **`An unparseable date value drops the filter instead of throwing`** — drive an invalid non-empty value into the field; the last call has no `since` key and the page still renders.
7. **`The non-date filters are sent verbatim`** — an event kind and an actor survive unconverted.

**Mechanics, and report what you observe rather than assuming:**
- `user.type()` on `type="datetime-local"` may be sanitised to `''` by jsdom mid-typing. Prefer `fireEvent.change(input, { target: { value: '...' } })`; if `user.type` works, use it for consistency with the existing test at `:82` and say so.
- `process.env.TZ` mutation at runtime **does** take effect on Node 22 — measured. Always restore in `afterEach`; never set `TZ` in `vite.config.ts`.

**Also confirm and report:** the five existing tests (`:63-111`) still pass.

**Done when:** `pnpm --filter @smart-sentinel-eye/management-web test` shows the seven new cases failing (or the subset that can fail before the fix — cases 5 and 7 may legitimately be green, and say which), and the **verbatim** output of at least case 1 is captured for the PR body.

**Do not touch `AuditPage.tsx`.**

### Phase 4b — GREEN (`frontend-engineer`)

**[T002] [US1] Convert the wall clock to an instant in `toQuery`.**

File: `apps/management-web/src/features/audit/AuditPage.tsx` only.

Apply plan.md §*The change, precisely*: add `toInstant(wallClock: string): string | undefined` with its why-comment, and re-point `toQuery`'s two date lines at it. Nothing else in the file changes at this task.

**You may not edit `AuditPage.test.tsx`.** If a test looks wrong, report it — do not adjust it.

**Done when:**
```sh
pnpm --filter @smart-sentinel-eye/management-web test        # all green, existing 5 included
pnpm --filter @smart-sentinel-eye/management-web typecheck
pnpm lint            # --max-warnings 0
pnpm format:check
```
all pass, and `git diff --stat` shows exactly `AuditPage.tsx`.

Commit: `fix(management-web): send audit date filters as instants, not bare local wall clocks`.

---

## US2 (P2) — The field says which clock it speaks

**Cuttable. If this spec must be trimmed, cut US2 — US1 closes the correctness gap on its own** (spec §US2, scope decision).

**[T003a] [US2] Red test for the zone-naming labels.** (`test-writer`)

File: `apps/management-web/src/features/audit/AuditPage.test.tsx`. One case: `The date filters name the clock they are interpreted in` — `screen.getByLabelText(/Since \(your local time\)/i)` and the *Until* equivalent resolve. Red today (the labels read `Since` / `Until`). Sequence after T001 — same file.

**[T003b] [US2] Name the zone on both date labels.** (`frontend-engineer`)

File: `apps/management-web/src/features/audit/AuditPage.tsx` — JSX only, two `FormField label=` strings (`:135`, `:138`). No logic. Sequence after T002 — same file.

**Done when:** the suite is green and `getByLabelText('Event kind')` and the other existing label queries still resolve.

Commit, separate from T002: `feat(management-web): name the local clock on the audit date filters`.

---

## Phase 5 — Verify (`/verify`)

**[T004] Observe the request, not only the tests.**

Follow spec §*Independent end-to-end test procedure*. The decisive artefact is **the request URL in the Network panel**, because a row set can match by coincidence:

- Before-state evidence: capture `since=2026-09-17T08%3A00` from the branch point (or quote the T001 red output as the equivalent, and say which you did).
- After-state evidence: `since=…T06%3A00%3A00.000Z` — an explicit `Z` — issued from a non-UTC browser zone, with the target row present in the table.

**Latency:** cite **N/A** — operator-initiated `GET`, none of constitution §IV's six legs (spec §Latency-budget impact).

Write `specs/210-the-clock-a-filter-meant/verification.md`, matching spec 209's. **Record every figure observed, including any that only went to the orchestrator** — a measurement reported in conversation and nowhere else is invisible to every later reader.

## Phase 6 — QA

**[T005] `/code-review` with `frontend-reviewer`.** Work plan.md §*Review focus for phase 6* — in particular #1, that the new test is genuinely red **in UTC** and not merely on a developer's non-UTC machine.

`/security-review` is **not** required; plan.md §*Security review trigger* has the reasoning. State the skip in the PR.

## Phase 7 — PR

**[T006] Open the PR against `develop`.**

```sh
gh pr create --base develop --title "fix(management-web): audit date filters and table share one clock (#2431)" ...
```

Body must carry:
- `Closes #2431` (a closing keyword — a bare mention auto-closes about one time in three).
- The **verbatim** phase-4a red output (ADR-0139) — the only form of that evidence a later reader can check.
- `Phase 6: /security-review skipped — client-side only, no auth/scope/endpoint touched.`
- The scope note: `ResourceTimelineInput.since/until` carry the same latent shape but have no caller (`useGetResourceTimelineQuery` is exported and unconsumed) — deliberately not changed.
- The footer from the session's attribution reminder. **No `Co-Authored-By` on the commits** — ADR-0086.

Then park and watch (ADR-0144); do not wait for CI.

---

## Task table

| ID | P | Story | Agent | Files | Depends on |
|---|---|---|---|---|---|
| T001 | | US1 | `test-writer` | `AuditPage.test.tsx` | — |
| T002 | | US1 | `frontend-engineer` | `AuditPage.tsx` | T001 (red observed) |
| T003a | | US2 | `test-writer` | `AuditPage.test.tsx` | T001 |
| T003b | | US2 | `frontend-engineer` | `AuditPage.tsx` | T002, T003a |
| T004 | | — | `/verify` | `verification.md` | T002 |
| T005 | | — | `frontend-reviewer` | — | T004 |
| T006 | | — | orchestrator | — | T005 |

**No `[P]` marker is set, deliberately** — every implementation task shares one of two files. The disjointness that matters here is *between specs*, and this one is disjoint from all backend work (see §Parallelism).
