# Plan 273: The places a click keeps

**Spec**: [spec.md](spec.md) · **Issue**: #2632 · **Pattern**: #2624 / spec 267 / PR #2631

## 1. Constitution and ADR alignment

| Check | Result |
|---|---|
| Bounded context / layers | None. This is a frontend-only change in `apps/management-web`: no backend, no `Shared.Contracts`, no AppHost. |
| ADR-0151 §1 (the rule) | Each site passes the test "activating it, or its own result, is what makes it unavailable" (spec §1 mechanism column). Overlays `:209` fails the test and is excluded [J1]. |
| ADR-0151 §2 (guard is part of the fix) | Every site gets a guard in the same change (§3). W3 guards the **form's** `onSubmit`. |
| ADR-0151 §3 (`Button`'s `unavailable`) | Used at all sixteen. No site hand-rolls `aria-disabled`, and none passes both `disabled` and `unavailable` (`Button.tsx:39-40`). |
| ADR-0151 §4 (no sweep) | These are named sites for a filed defect. Up/Down and the unmount cases are reported, not converted. |
| ADR-0036 (smallest change, no speculative generality) | The guard is **repeated inline at each site**, and not extracted into a shared hook or helper. This is the precedent #2624's phase 6 set in writing (PR #2631: "the five near-identical `handleFormSubmit` guards are not extracted into a shared hook… a new abstraction here would be a design decision this run may not make"). This spec has more copies, but that does not change the answer: extracting one is an abstraction decision for its own issue, not a drive-by. |
| ADR-0077 / spec 268 (`busy`) | Untouched. W3 keeps `busy={isLoading}`, and no other site gains `busy` or a cursor class. |
| ADR-0109 (disjoint files) | One production file per story. The unit tests are one file per story. The e2e file is shared, so it has one writer (T010). |
| §IV latency | N/A (spec header). |
| New ADR | None needed. |

## 2. The shape, mirrored from #2624

The conversion at each site is **two lines**, with no restructuring:

1. `disabled={X}` → `unavailable={X}`, where X is character-for-character today's expression.
2. The guard `if (X) return;` goes first in the control's handler. W3 is the one exception: it guards
   the form (§3.7).

Where X is an inline expression that would otherwise be written twice (S1, C1, C2, A1, W2), the
engineer **may** hoist it into one well-named local beside the JSX (`const previousUnavailable = …`).
That keeps the prop and the guard from drifting apart. This is an allowed refactor within the changed
lines, not a requirement, and it adds no new helper, hook or file.

Where a handler is an inline arrow (`onClick={() => void publishRevision(...)}`), it becomes a block
arrow with the guard first: `onClick={() => { if (disabled) return; void publishRevision(...); }}`.
Nothing is moved out of the JSX.

## 3. Per-site plan

### 3.1 US1: `systemVariables/SystemVariablesPage.tsx`
- **S1 (`:167`)**: `unavailable={inProgress || editValue === undefined || editValue === ''}`. The guard
  goes in `onValueSubmit` (`:52`) as its **first** statement, `if (saving) return;`, and the existing
  `if (raw === undefined) return;` becomes `if (raw === undefined || raw === '') return;` [J3]. The
  guard lives in the handler rather than the inline arrow because `onValueSubmit` already holds the
  `raw` check. `inProgress` is `saving` (`:138`).
- **S2 (`:174`)**: `unavailable={inProgress || archiving}`. The inline `onClick` gains
  `if (inProgress || archiving) return;` before `setArchiveFor(...)`.

### 3.2 US2: `layouts/LayoutsPage.tsx`
- **L1 (`:226`) Publish** and **L2 (`:247`) Edit (new draft)**: `unavailable={disabled}`, with
  `if (disabled) return;` first in each `onClick`.
- **L3 (`:261`) More actions trigger**: `unavailable={disabled}`. See §3.3 for the guard.
- The menu entries' `disabled` (`:172,187,203`) **stay**.

### 3.3 L3's guard, in detail
The trigger is a Radix `DropdownMenu.Trigger asChild` around our `Button`. On `aria-disabled` it still
opens the menu, because Radix reads its own `disabled` prop [J2]. The action the operator could take
from there is selecting an entry, and every entry is `disabled` on the **same** local. Radix's `Item`
with `disabled` fires no `onSelect`, and roving focus skips it. So the refusal already exists, in the
spec-154 shape ("the handler already no-ops"). The 4a test must prove it: with `disabled` true, open the
menu and try to select Revert, and `revertRevision` is not called. If that test can't be made red by a
counterfactual (remove the entries' `disabled`), the spec-154 claim is false. **Stop and re-plan**; do
not bolt on a trigger guard.

### 3.4 US3: `overlays/OverlaysPage.tsx`
O1 `:169`, O2 `:184`, O3 `:231`, O4 `:246`, O5 `:267` each become `unavailable={disabled}` with
`if (disabled) return;` first in their `onClick`. O3's `onClick` is
`() => void onEdit(chain, live ?? newest!)`, and the guard goes before it. **`:209` Edit draft is
unchanged** [J1].

### 3.5 US4: `cameras/CamerasPage.tsx`
C1 `:177` and C2 `:184`: `unavailable={<today's expression>}`, with the same expression as the guard in
each `onClick`. Without C1's guard, clicking an unavailable Previous on page 1 is harmless
(`Math.max(0, …)`), but a click during `isFetching` would queue a second page move. Without C2's guard,
a click on the last page would page past the end. The guards are therefore behavioural, not decorative.

### 3.6 US5: `audit/AuditPage.tsx`
A1 `:200`: `unavailable={data?.nextCursor === null || data?.nextCursor === undefined}`, and the guard
goes in the `onClick` before `setApplied`. **This guard prevents a real regression.** The handler
computes `cursor: data?.nextCursor ?? undefined`, so an unguarded activation on the last page sets
`cursor: undefined`, which is **the first page** (spec §3).

### 3.7 US6 and US7: `walls/`
- **W1 `WallDetailPage.tsx:126`**: `unavailable={switchState.isLoading}`, with
  `if (switchState.isLoading) return;` first in `onClick`.
- **W2 `WallDetailPage.tsx:157`**: `unavailable={switchState.isLoading || scene === wall.showing}`,
  with the same expression as the guard.
- **W3 `WallForm.tsx:162`**: `<Button type="submit" unavailable={isLoading} busy={isLoading}>`. The
  `<form onSubmit={onSubmit}>` (`:87`) becomes `onSubmit={handleFormSubmit}`:
  ```ts
  function handleFormSubmit(event: FormEvent) {
    if (isLoading) {
      event.preventDefault();
      return;
    }
    void onSubmit(event);
  }
  ```
  This mirrors `RegisterCameraDialog.tsx:107-113` exactly, including the ADR-0151 comment's intent.
  `onSubmit` is RHF's `handleSubmit(...)` (`:76`), which already calls `preventDefault` on the path it
  takes.

## 4. Tests (Phase 4a)

### 4.1 jsdom, one existing test file per story
Each file already mocks its page's RTK hooks. Extend that mock so the relevant `isLoading` /
`isFetching` / `data` is **mutable** per test, the way `RegisterCameraDialog.test.tsx` and spec 268's
`WallForm.test.tsx` (`createWallMutationState.current`) do. Per converted control:

- **(a) attribute**: in the unavailable state, `aria-disabled="true"` and no `disabled` attribute.
  **Red** on `develop`.
- **(b) guard**: in the unavailable state, `fireEvent.click` (or `user.click`; Testing Library clicks
  an `aria-disabled` button) calls neither the mutation nor the state setter. **Green pin** on `develop`
  (native `disabled` blocks the click), and discriminating only by counterfactual (T014).
- **(c) available**: outside the window the control is `aria-disabled="false"` (not absent; see
  `Button.test.tsx:110`) and activates as today. **Green pin.**

**Declared rewrite, not an edit to evade:** `cameras/CamerasPage.test.tsx:152` asserts
`toBeDisabled()` on Previous. Testing Library's `toBeDisabled` reads native `disabled` only, so this
assertion's **mechanism** moves with FR-001. Spec 160 T003 and spec 267 set the precedent: rewrite it in
4a to `toHaveAttribute('aria-disabled', 'true')` plus `not.toHaveAttribute('disabled')`. The claim it
pins (Previous is unavailable on page 1) is unchanged. It is the only such assertion (searched:
`toBeDisabled|toBeEnabled|\.disabled` across `features/**/*.test.tsx` and `e2e/`).

**W3** gets all three cases (a) (b) (c), as `RegisterCameraDialog.test.tsx` has them. Its existing
`aria-busy` case (`WallForm.test.tsx:124`) stays **unmodified**.

### 4.2 Real browser: `e2e/in-flight-focus.spec.ts`
Extend spec 267's file with one `test.describe` per story. Reuse its file-local `holdWrites` as-is: it
takes a method, so it holds `GET` for pagination too. There is one test **per mechanism per page**, not
per control; FR-004 binds mechanisms, and ten tests already cover every mechanism on every page:

| Test | Control(s) exercised | Held request |
|---|---|---|
| System variables: Set value | S1 | `PUT …/{name}/value` (`systemVariables.api.ts:185`) |
| System variables: Archive focus-return | S2 | `POST …/{name}/archive` (`:237`) |
| Layouts: Publish | L1 | `POST …/revisions/{n}/publish` (`layouts.api.ts:114`) |
| Layouts: More actions → Revert | L3 | `POST …/revisions/{n}/revert` (`layouts.api.ts:159`) |
| Overlays: Publish | O1 | `POST …/revisions/{n}/publish` (`overlays.api.ts:86`) |
| Overlays: Archive focus-return | O5 | `POST …/revisions/{n}/archive` (`overlays.api.ts:97`) |
| Cameras: Next | C2 (in flight) | `GET` cameras page |
| Audit: Next to the last page | A1 (terminal) | `GET` audit, **fulfilled** by `page.route` with a first page (`nextCursor: 'x'`) and then a last page (`nextCursor: null`), because a fresh CI stack cannot be relied on for more than 50 rows |
| Walls: Show | W2 | `POST /walls/{id}/switch` (`walls.api.ts:49`) |
| New wall: Save + implicit submission in Name | W3 | `POST /walls` (`walls.api.ts:102`) |

Every hold is released in `finally`. Waits are by condition only (ADR-0150). Where the test needs data
(two published layouts, a wall, a draft), reuse the existing seeding in `e2e/support/`
(`seed-published-layout.setup.ts`, the walls specs' setup) and do **not** write new seeding modules. If
the Cameras test needs more than 50 cameras, fulfil the `GET` the way Audit does instead of seeding 51.

Expected on `develop`: every `toBeFocused` is **red** (received `<body>`), and every `count() === 1` and
state-unchanged assertion is a **green pin**.

## 5. Risks

- **Playwright actionability.** Playwright treats `aria-disabled="true"` as not enabled, so `.click()`
  waits and then times out on an unavailable control. The e2e must activate with `keyboard.press('Enter')`
  on the focused control, as spec 267's file does. Existing specs that click these controls only click
  them while they are available, so they are unaffected.
- **Two unmerged branches claiming 273.** Re-check before the PR (memory: "spec number: origin/develop
  isn't enough").
- **Shard filters.** No new test **class** is added, only cases in existing vitest files and one existing
  e2e file, so no `shard-N.filter` entry is expected. Re-check if the test-writer creates a new file.
