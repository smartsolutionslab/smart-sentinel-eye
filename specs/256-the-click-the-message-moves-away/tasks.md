# Tasks 256 — The click the message moves away

`spec.md` / `plan.md`. Issue
[#2366](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2366)
(the feature-level issue; **no per-task issues** — practice stopped at spec 028;
do not run `/speckit-taskstoissues`). Branch `fix/2366-mousedown-blur-submit-race`,
worktree `D:/Github/sse-2366`, cut from `origin/develop`.

**Phase-4a colour: RED** (behaviour-changing bug fix, spec §7). Every new test
below must be observed **failing** before T005 exists, and that output quoted
verbatim in the PR. A new test arriving green is a phase-4 failure. The existing
tests listed in plan §4.3 are the regression net: captured green in T003, green
again in T006, **unmodified**.

**Phase roles:** 4a `test-writer`, 4b `frontend-engineer`, 5 `frontend-engineer`
(`/verify`), 6 `frontend-reviewer`.

---

## 1. US1 (P1) — one click on Save saves, whatever the geometry panel shows next

### Phase 4a — `test-writer` (tests only; may not be edited afterwards to pass)

| ID | [P] | Story | Task | File | Depends on |
|---|---|---|---|---|---|
| **T001** | [P] | US1 | New `describe` block, plan §4.2's four cases: every field message reserved before shown (test id `overlay-geometry-message-slot-<field>`, `[aria-hidden="true"]` texts = literal expected set per field); every advisory wording reserved (`overlay-geometry-advisory-slot`, three FR-012 literals); reserved copies invisible to AT (no `alert` with nothing refused, `status` text `''`, exactly one `alert` after a refusal); a refusal adds no direct child to the Width column (located via `closest('label').parentElement`). Literals in the test, never imported from the component. Existing cases untouched. | `apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx` | — |
| **T002** | [P] | US1 | Plan §4.1's four Playwright tests: refused Width `0` + one click saves (read back `normalizedWidth === 0.3`); Left `90` + one click saves (read back `normalizedX === 0.9`); Save `boundingBox().y` unchanged across a refusal on blur; unchanged across the advisory appearing (Left `90`) and clearing (Left `10`), then Cancel. Reuse `signInAsOperator`, cold-stack timeouts, `E2E ` name prefix, the gateway read-back pattern at `:79-101,125-142`. Use a **size** refusal (multi-line wrap), never `Enter a number.`. | `e2e/overlays.spec.ts` | — |
| **T003** | — | US1 | Run and **capture verbatim**: (a) `pnpm --filter @smart-sentinel-eye/shared test -- OverlayGeometryFields` — T001's cases red (expected: `getByTestId` throws / child count differs), every pre-existing case green; (b) the rest of the regression net (plan §4.3) green; (c) with the Aspire stack up — orchestrator confirms no sibling stack is running first — `pnpm exec playwright test e2e/overlays.spec.ts --project=chromium`: T002's four red (expected: list-name timeout ×2, differing `y` ×2), pre-existing cases green. If any new test is green, or any failure is a type/import error rather than the asserted behaviour, stop and hand back. Return all output verbatim as the engineer's brief. | — | T001, T002 |

T001 and T002 own disjoint files → `[P]`.

### Phase 4b — `frontend-engineer` (may not edit T001/T002 or any existing test)

| ID | [P] | Story | Task | File | Depends on |
|---|---|---|---|---|---|
| **T004** | — | US1 | Behaviour-preserving prep inside the file (plan §2.1): `NOT_A_NUMBER_MESSAGE`, `rangeMessage`, `messagesFor`, advisory constants + `ADVISORY_WORDINGS`; `commit`/`validate`/`buildAdvisory` read them. Strings byte-identical. Pre-existing tests green, T001 still red for the same reason. | `apps/shared/src/ui/composites/OverlayGeometryFields.tsx` | T003 |
| **T005** | — | US1 | Plan §2.2-2.3: file-local `ReservedMessageSlot`; wrap each field's alert and the advisory status span, unchanged, inside it with matching text styles and the two test ids. Inline styles, as the file already does. Doc-comment the component with the *why* (the mousedown/mouseup race), not the issue number. | same | T004 |
| **T006** | — | US1 | Re-run T003's three commands: T001 + T002 green, regression net green **with zero test edits**; `pnpm lint`, `pnpm typecheck`, `pnpm format:check` clean (`typecheck:e2e` failing identically on clean `develop` is pre-existing — prove by stash, don't touch config). | — | T005 |

T004 → T005 share one file, so sequential. Commits (ADR-0030): T001+T002 as
`test(shared,e2e): …` red-first; T004 `refactor(shared): …`; T005 `fix(shared): …`
— each commit builds on its own (rebase-merge lands them individually).

### Phase 5 — verification (`frontend-engineer`, `/verify`)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T007** | — | US1 | Spec §6 by hand in Chromium against the running stack: steps 1-5, noting the Save button's `getBoundingClientRect().top` before/after in step 5, and a screenshot of the dialog at rest showing the reserved band (plan §5). Write `specs/256-the-click-the-message-moves-away/verification.md` quoting T003 red, T006 green and the manual observations verbatim. Latency: N/A (spec header). | T006 |

### Phase 6 — `frontend-reviewer`

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T008** | — | US1 | Review: one production file changed? Hidden copies use the *same* style object as the live message? `aria-hidden` on every copy, and no role on any? `role="alert"` still mounted with its content; `role="status"` still always mounted? Strings byte-identical to spec 151? Existing tests unmodified since T003 (diff them)? T001 assertions can fail if the reservation is wrong — not checking their own input? No `FormField`/`Dialog`/RHF change crept in? | T007 |

### Phase 7

| ID | Task | Depends on |
|---|---|---|
| **T009** | Re-check spec number 256 against `origin/develop`, every remote branch and sibling worktrees (251, 253, 254×3, 255 claimed on 2026-09-25). `gh pr create --base develop`; body quotes T003's red and T006's green, links `verification.md`, and says **`Fixes #2366`**. Rebase-only (ADR-0087). Confirm #2366 closed after merge. | T008 |

### Follow-up, not part of this slice

| ID | Task |
|---|---|
| **F001** | File an issue: should Save be refused (with the panel's message focused) while a geometry field holds a refused draft? Spec 151 FR-010 currently saves the last committed value, silently discarding what the operator typed. Product question, not a defect of this spec. |
| **F002** | Optional: if a blur-validated RHF form (`mode: 'onBlur' \| 'onTouched' \| 'all'`) is ever proposed, it reintroduces this race via `FormField` (spec §2.2/§8). No guard now. |

---

## 2. Independent test criterion ("done")

In real Chromium, typing an invalid or an off-edge value into a geometry field
and clicking Save **once** saves the overlay; the Save button's `y` is identical
before and after any geometry message appears or clears. Not "the suite is
green".

## 3. Foundational / blocking

**None.** No `Shared.Kernel`, `Shared.Contracts`, `AppHost`, Aspire resource or
migration. The only shared resource is the Aspire stack for T003/T006/T007 (one
machine, one stack).

## 4. Parallelism (ADR-0109)

T001 ∥ T002 (disjoint files). Everything after T003 is sequential: T004/T005
share one file, and T006-T009 are gates on it.
