# Tasks 163 — a Button that keeps focus

**Phase:** 3 (Tasks) — ADR-0037 · **Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2399](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2399) · **Branch:** `feat/2399-a-button-that-keeps-focus` (from `origin/develop` @ `5ea913dc`)
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Board gate:** issue #2399 must be on Project #13 —
`gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2399`
(verify with `--limit 2000`; `item-list` defaults to 30). Feature-level issue only — **no
per-task issues** (`/speckit-taskstoissues` is not run; CLAUDE.md phase-3 row).

`[P]` = safe to run concurrently; the marked tasks own **disjoint files** (ADR-0109).

---

## Ordering at a glance

```
T001 ──┬── T003 [P] ──┐
       └── T004 [P] ──┤   characterisation, GREEN on unmodified develop
                      │
T002 (RED, new tests) ─┴─→ T005 (Button.tsx) ─┬─ T006 [P] ─┐
                                              └─ T007 [P] ─┤ → T008 → T009 → T010 → T011 → T012
T008 [P] (ADR figure) ───────────────────────────────────────┘
```

**T005 is the foundational task.** `Button.tsx` must carry the prop before either call
site can compile against it, so T006/T007 cannot start until T005 lands. Everything
before T005 and T008 is independent of it.

---

## US1 (P1) — the sanctioned path is the easy one

### `[T001]` `[US1]` Capture the baseline, and prove the new test file will be collected

Before writing anything:

```sh
cd apps/shared        && npx vitest run  2>&1 | tail -20
cd apps/management-web && npx vitest run src/features/overlays src/features/layouts 2>&1 | tail -20
```

Record both as green. Then confirm `src/**/*.test.tsx` under
`apps/shared/src/ui/primitives/` is collected at all, by pointing at the neighbour that
already exists:

```sh
cd apps/shared && npx vitest list --run | grep primitives
```

Memory *tests that actually run*: a test file the config does not collect is not
coverage, and this repo shipped one such file for weeks. `apps/shared/vitest.config.ts`
includes `src/**/*.test.tsx`, so `Button.test.tsx` will match — **verify it by the tool
in T004, not by reading the config**.

**Depends on:** nothing.

---

### `[T002]` `[US1]` **4a-RED** — write `apps/shared/src/ui/primitives/Button.test.tsx`

**BEHAVIOUR-CHANGING → the tests must be observed RED.** Written by `test-writer`,
which returns the **verbatim** vitest output; the engineer receives that output as its
brief and may not edit these tests to pass (ADR-0144).

New file. Mirror `ConfirmDialog.test.tsx` in the same directory: `// @vitest-environment
jsdom` as line 1, `@testing-library/react`, `afterEach(cleanup)`, `vi.fn()` handlers,
`import { Button } from './Button.js';`.

Five cases, one per acceptance scenario in spec §6:

1. **`unavailable` announces without natively disabling** — `aria-disabled="true"`,
   `not.toHaveAttribute('disabled')`, `toHaveClass('aria-disabled:opacity-50')`.
2. **An unavailable Button keeps the operator's place** — `.focus()` then
   `document.activeElement` is the button; `fireEvent.click` then the `vi.fn()` `onClick`
   **was called**. (Called, not refused — `aria-disabled` is an announcement and the
   caller's guard is what refuses. ADR-0151 §1.)
3. **`unavailable={false}` renders `aria-disabled="false"`** — the value, not the
   absence. Spec §1.5: seven assertions in five untouched suites in `apps/` depend on
   this (ten counting `e2e/overlays.spec.ts`).
4. **No `unavailable` prop ⇒ unchanged** — no `aria-disabled`, no
   `aria-disabled:opacity-50`, and `disabled:opacity-50` +
   `disabled:pointer-events-none` still present. **This one is expected GREEN on
   `develop`** and is a pin on what must not move; declaring it here in advance means
   its green result is not mistaken for a 4a failure.
5. **`unavailable` never reaches the DOM** — `not.toHaveAttribute('unavailable')`.

**Expected red: 1, 2, 3, 5.** Expected green: 4.

**Two things the test-writer must expect and report accurately:**

- vitest transpiles with esbuild and does not typecheck, so the file runs against a
  `Button` with no such prop and fails on **assertions**. Those assertion failures are
  the red.
- `npm run typecheck` will separately report `Property 'unavailable' does not exist on
  type 'ButtonProps'` for the whole of phase 4a. **That is not the red** and must not be
  quoted as it.

**Depends on:** T001.

---

### `[T005]` `[US1]` **FOUNDATIONAL** — add `unavailable` to `Button.tsx`

Four edits (plan §2), nothing else:

1. `unavailable?: boolean` on `ButtonProps`, with the FR-006 doc comment.
2. Destructure it out of the props (FR-004 — it must not reach `...rest`).
3. `aria-disabled={unavailable}` on `<Component>`, **before** `{...rest}`.
4. `clsx(base, variants[variant], unavailable !== undefined && 'aria-disabled:opacity-50', className)`
   — caller `className` stays last.

**Do not touch `base`.** `disabled:pointer-events-none disabled:opacity-50` stay
(FR-005; ADR-0151 rejected stripping them, and case 4 of T002 pins them).

**Do not add** `pointer-events-none` for `unavailable`, a `disabled`/`unavailable` union
type, a dev-time warning, or any interception of `onClick` — spec §4 Q1 is explicit that
the last of those is a `[DECISION REQUIRED]`, not this slice's to take, and that it
would neutralise spec 160's counterfactual.

**The doc comment is the substance of this task** (ADR-0151 Implementation Notes: "Put
it where the next author will be"). It must state, referencing ADR-0151 by number:

- **When to reach for it:** a control that can become unavailable **while it holds
  focus** — canonically, one that activating is what makes unavailable. A browser blurs
  a natively-disabled element to `<body>`, and the operator's next `Tab` starts from the
  top of the document.
- **When `disabled` is still right:** a control that cannot be focused when it disables.
- **The guard is mandatory.** `aria-disabled` without one is a regression, not a partial
  fix. Nothing in this prop enforces it, and nothing can — the guard is a statement
  inside a handler body, or on a different element entirely.
- **For a submit button the guard belongs on the form's `onSubmit`**, before validation,
  because implicit submission never goes through the button's `onClick`.
- **A new submit-button site owes a real-browser Playwright test** pressing `Enter` in a
  **text field** (not on the button), proven by disabling the guard and watching the
  request count rise. A `user-event` Enter test measures a synthetic click and proves
  nothing.
- **Do not pass `unavailable` and `disabled` together** — the browser enforces
  `disabled` and the control loses focus anyway.
- Point at the three reference implementations: `OverlayEditor.tsx` Undo/Redo (handler
  already no-ops), `ChainRecoveryNotice.tsx` Retry/Reload (`if (reReading) return;`),
  `OverlayEditorDialog.tsx` Save (the form's `onSubmit`).

**Done when:** all five T002 cases are green and the T003/T004 characterisation
assertions are still green **unmodified**.

**Depends on:** T002 (red observed and quoted).

---

### `[T009]` `[US1]` Run the two counterfactuals that prove T002 discriminates

Constructed in the **subject's** file (`Button.tsx`), with the expectations in
`Button.test.tsx` — spec 162's criterion. Revert each immediately after capturing the
output.

**D1/D2 —** replace `aria-disabled={unavailable}` with `disabled={unavailable}`.
Expect cases 1, 2 and 5 red; case 2's focus and onClick failures are the ADR's actual
claim being proved.

**D5 —** stop destructuring `unavailable`, letting it fall into `...rest`. Expect case 5
red.

Quote both verbatim in the PR body (ADR-0139; ADR-0151 Implementation Notes: "remove the
guard, watch the test fail").

**Depends on:** T005.

---

## US2 (P1) — the prop has a real consumer

### `[T003]` `[P]` `[US2]` **4a-CHARACTERISATION** — pin the overlay Save button, GREEN first

`apps/management-web/src/features/overlays/OverlayEditorDialogSaveGate.test.tsx`.
At the existing `expect(saveButton).toHaveAttribute('aria-disabled', 'true')` (`:237`),
append:

```ts
expect(saveButton).toHaveClass('aria-disabled:opacity-50', 'aria-disabled:cursor-progress');
expect(saveButton).not.toHaveAttribute('disabled');
```

**Order-independent `toHaveClass(a, b)`, never an exact `className` string** — `clsx`
order changes by construction when the class moves from the call site into the
primitive, and an exact-string assertion would go red for a reason that is not a
behaviour change (plan §3).

**Must be captured GREEN on unmodified `develop`** and quoted as such, then pass
**unmodified** after T006. An assertion that has to be edited is evidence the behaviour
moved: block, do not adjust (ADR-0144).

**Depends on:** T001. **`[P]` with T004** — different file, different feature folder.

---

### `[T004]` `[P]` `[US2]` **4a-CHARACTERISATION** — pin the layout Save button, GREEN first

Identical to T003, in
`apps/management-web/src/features/layouts/LayoutEditorDialogSaveGate.test.tsx` at the
existing `aria-disabled` assertion (`:217`).

Also, in the same run, confirm by the tool rather than the config that both files are
collected:

```sh
cd apps/management-web && npx vitest list --run | grep SaveGate
```

**Depends on:** T001. **`[P]` with T003.**

---

### `[T006]` `[P]` `[US2]` Adopt `unavailable` at the overlay editor dialog's Save

`apps/management-web/src/features/overlays/OverlayEditorDialog.tsx` (~`:356-363`):

```diff
-    aria-disabled={saveBlocked}
-    className="aria-disabled:opacity-50 aria-disabled:cursor-progress"
+    unavailable={saveBlocked}
+    className="aria-disabled:cursor-progress"
```

Update the comment block immediately above to name the prop and cite ADR-0151 instead of
re-deriving the blur behaviour — that replacement is what ADR-0151's Consequences claim
the prop buys. Keep the pointer to spec 160 / issue #2387 for provenance.

**Untouched (FR-007), and a diff touching any of them is a review blocker:**
`saveBlocked`'s definition (`:249-274`), `handleFormSubmit` (`:277-285`), `saveRef`
(`:247`), the form's `onSubmit` wiring. The form guard is the only thing between this
codebase and the implicit-submission trap.

**Done when:** T003's assertions pass unmodified, and the file's five existing suites
(`OverlayEditorDialog`, `…SaveGate`, `…ChainRecovery`, `…ChainRetention`,
`…ResolvePreview`) pass unmodified.

**Depends on:** T003, T005. **`[P]` with T007** — disjoint files.

---

### `[T007]` `[P]` `[US2]` Adopt `unavailable` at the layout editor dialog's Save

Identical change in `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`
(~`:460-466`), same untouched list (`saveBlocked` ~`:267`, `handleFormSubmit` ~`:311`,
`saveRef`).

**Done when:** T004's assertions pass unmodified, and `LayoutEditorDialogSaveGate`,
`…ChainRecovery`, `…ChainRetention`, `…Retention` pass unmodified.

**Depends on:** T004, T005. **`[P]` with T006.**

---

### `[T010]` `[US2]` Run the characterisation counterfactuals D3 and D4

Again constructed in `Button.tsx`, with the expectations two packages away.

**D4 —** drop `'aria-disabled:opacity-50'` from the `clsx` call. Expect T003 and T004
red.

**D3 —** emit `aria-disabled={unavailable || undefined}`. Expect the **seven existing**
`toHaveAttribute('aria-disabled', 'false')` assertions red, across five suites nobody
edited for this change:

```
OverlayEditorDialogChainRecovery.test.tsx:237,532   LayoutEditorDialogChainRecovery.test.tsx:214,508
OverlayEditorDialogChainRetention.test.tsx:148      LayoutEditorDialogRetention.test.tsx:210
OverlayEditorDialogResolvePreview.test.tsx:246
```

D3 is the strongest discrimination in the slice: those assertions were written for other
reasons entirely, so they cannot have been shaped to fit this change. Quote it.

Revert both. **Depends on:** T006, T007.

---

## US3 (P2) — the record matches the tree

### `[T008]` `[P]` `[US3]` Correct ADR-0151's population figure

`docs/adr/0151-a-disable-that-keeps-the-operators-place.md`. Two places, **no decision
text changed**:

- **Context → "The population":** 32/17 → **26 native `disabled={…}` sites across 13
  files**, plus 6 `aria-disabled` across 4, **32 across 17 in total**. State why the
  original was wrong — the substring `disabled=` matches `aria-disabled=`, so the two
  bullets double-counted the six — and include the reproduction command from spec §1.3.
  Note that the tree has **not** moved since `36796e84`: the same command gives the same
  numbers at both SHAs, so this is an arithmetic correction, not drift.
- **Decision §4:** "The 32 native sites" → "The 26 native sites".

Nothing else in the ADR changes. This is a measured figure, not a decision — the lane's
"may not write an ADR or amend the constitution" prohibition is about deciding, and
correcting arithmetic in an Accepted ADR is what this repository has had to do for §II
(twice), the phase-3 board gate and §IV's leg table. Say so in the commit body.

**Depends on:** nothing. **`[P]` with everything** — it is the only task touching
`docs/`.

---

## Closing tasks

### `[T011]` Gate check — nothing weakened, nothing swept

```sh
git diff origin/develop --stat
```

Assert, by reading the output:

- **Exactly 7 files**: `Button.tsx`, `Button.test.tsx`, the two dialogs, the two
  `…SaveGate.test.tsx`, `0151-….md` — plus this spec directory.
- **`e2e/` is absent.** Spec §4 Q2: `e2e/overlays.spec.ts:426-451` is the only real
  implicit-submission proof in the repository and must pass **unmodified**.
- **No file under `apps/management-web/src/features/` other than the two dialogs and
  their two test files.** The 26 native sites are untouched (ADR-0151 §4); three of them
  (`OverlaysPage`, `LayoutsPage`, `ConfirmDialog`) are neighbours of files in scope.
- **No test deleted, no threshold lowered, no suppression added, no analyzer narrowed**
  (ADR-0144).
- `apps/kiosk-web/` untouched.

Then, per workspace: `npm run lint`, `npm run typecheck`, `npm run test`.

**Depends on:** T006, T007, T008, T009, T010.

---

### `[T012]` Phase-5 verification note

Spec §7's five-step procedure, with steps 3 and 4 observed against a booted stack
(memory: *one machine, one Aspire stack*; *stop the stack before building*):

- Two browser contexts on the same overlay draft, first writer saves, second writer
  clicks Save; **while the PATCH is in flight, press `Tab`** — focus moves to the control
  *after* Save, not to the top of the document. That is ADR-0151 §1's claim, observed
  rather than asserted.
- In the same window, click into the overlay text field and press `Enter` — **no second
  PATCH** in the network panel. The trap, observed by hand.

**Latency budget (§IV): N/A** and stated as such in the note — no leg of the
event→overlay path is touched, and `Button` is not rendered by `kiosk-web`.

**Depends on:** T011.

---

## Declarations (ADR-0144), restated for the record

1. **WHICH ENGINEER: `frontend-engineer`** (reviewer: `frontend-reviewer`). No backend,
   no infra, no Aspire resource, no CI change, no migration.
2. **NEW ADR: no.** ADR-0151 is Accepted and its §3 specifies this prop exactly. The
   guard-coupling question is **escalated as `[DECISION REQUIRED]`** (spec §4 Q1, §9) and
   **not built**; the slice ships complete without it, so this is an escalation, not a
   BLOCK. T008 corrects arithmetic, not a decision.
3. **PHASE 4a, per work item:**
   - **T002 / T005 (`Button` + its new test) — BEHAVIOUR-CHANGING → RED.** Cases 1, 2 and 3
     observed failing before `Button.tsx` is edited; case 4 declared green in advance.
     Verbatim vitest output in the PR. Discrimination proved by T009 (D1/D2, D5),
     constructed in `Button.tsx`, asserted in `Button.test.tsx`.
   - **T003 / T004 / T006 / T007 (the two call sites) — BEHAVIOUR-PRESERVING →
     CHARACTERISATION, OBSERVED GREEN FIRST.** Captured green on unmodified `develop`,
     passing unmodified afterwards, alongside five existing suites and
     `e2e/overlays.spec.ts`. Discrimination proved by T010 (D4, and D3 against seven
     assertions nobody wrote for this change), constructed in `Button.tsx` in
     `apps/shared`, asserted in `apps/management-web`.
   - **T008 — documentation, no test.** The reproduction command is the evidence; a
     reviewer runs it. No test asserts the prose (memory: *guards that read the design
     artefact*).
