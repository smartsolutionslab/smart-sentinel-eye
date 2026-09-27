# Spec 273: The places a click keeps

**Issue**: #2632 (feature-level; no per-task issues) · **Filed from**: #2624's delivery (spec 267, PR #2631),
whose spec §1 "same-shape sites outside the issue's list" is this issue's inventory.
**Lane**: supervised (ADR-0037). Phases 1–3 only in this artifact set.
**ADRs**: **ADR-0151** (the rule this applies), ADR-0077 (the `Button` primitive and its `unavailable`
prop), ADR-0074 (management-web), ADR-0139 / §Testing (new behaviour observed red), ADR-0036 (smallest
change; mirror the existing pattern), ADR-0109 (disjoint files), ADR-0150 (waits by condition).
**New ADR needed**: **No.** ADR-0151 decides the rule, the guard obligation and the real-browser test
obligation. Its §4 ("not converted en masse") binds sweeps; this spec converts named sites for a filed
defect, as spec 267 did.
**Latency budget (constitution §IV)**: **N/A.** Operator console only; nothing on the event→overlay
path, nothing in `kiosk-web`.

## 1. The premise, re-checked against `1a51bd18` (origin/develop, 2026-09-27)

The issue's list **has drifted in three directions**: two files were added to the tree after #2624's
inventory, one file's controls were restructured, and one control on the list does not meet ADR-0151 §1.

Search used: `git grep -nE '(^|[^-])disabled[=:]' -- 'apps/**/*.tsx'`, each hit read against its
handler. `kiosk-web` has no native `disabled` site.

### In scope: sixteen controls in seven files

"Mechanism" says how the control comes to hold focus when it disables. *Direct*: activating it starts
the request that disables it. *Focus-return*: it opens a `ConfirmDialog`, whose `onConfirm` fires the
mutation and closes the dialog at once; `ConfirmDialog`'s `onCloseAutoFocus`
(`ConfirmDialog.tsx:77-80`) refocuses the opener, which is natively disabled by then. *Terminal*: the
activation's own result makes it unavailable (last page reached, scene now showing).

| # | File:line | Control | Today | Mechanism |
|---|---|---|---|---|
| S1 | `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx:167` | Set value | `disabled={inProgress \|\| editValue === undefined \|\| editValue === ''}` | direct, **and** terminal (success clears the edit buffer, so it stays unavailable) |
| S2 | `…/systemVariables/SystemVariablesPage.tsx:174` | Archive | `disabled={inProgress \|\| archiving}` | focus-return |
| L1 | `apps/management-web/src/features/layouts/LayoutsPage.tsx:226` | Publish | `disabled={disabled}` | direct |
| L2 | `…/layouts/LayoutsPage.tsx:247` | Edit (new draft) | `disabled={disabled}` | direct (`branchDraft`) |
| L3 | `…/layouts/LayoutsPage.tsx:261` | More actions (menu trigger) | `<Button variant="ghost" disabled={disabled} …>` | direct via the menu: `DropdownMenu.tsx:59` focuses the trigger, then Revert fires; also focus-return for Discard draft / Archive |
| O1 | `apps/management-web/src/features/overlays/OverlaysPage.tsx:169` | Publish | `disabled={disabled}` | direct |
| O2 | `…/overlays/OverlaysPage.tsx:184` | Discard draft | `disabled={disabled}` | focus-return |
| O3 | `…/overlays/OverlaysPage.tsx:231` | Edit (new draft) | `disabled={disabled}` | direct (`branchDraft`) |
| O4 | `…/overlays/OverlaysPage.tsx:246` | Revert | `disabled={disabled}` | direct |
| O5 | `…/overlays/OverlaysPage.tsx:267` | Archive | `disabled={disabled}` | focus-return |
| C1 | `apps/management-web/src/features/cameras/CamerasPage.tsx:177` | Previous | `disabled={offset === 0 \|\| isFetching}` | direct (`isFetching`), and terminal on page 1 |
| C2 | `…/cameras/CamerasPage.tsx:184` | Next | `disabled={offset + items.length >= totalCount \|\| isFetching}` | direct, and terminal on the last page |
| A1 | `apps/management-web/src/features/audit/AuditPage.tsx:200` | Next | `disabled={data?.nextCursor === null \|\| data?.nextCursor === undefined}` | terminal (disables once the page it loaded is the last) |
| W1 | `apps/management-web/src/features/walls/WallDetailPage.tsx:126` | Next (scene) | `disabled={switchState.isLoading}` | direct |
| W2 | `…/walls/WallDetailPage.tsx:157` | Show | `disabled={switchState.isLoading \|\| scene === wall.showing}` | direct, and terminal (the shown scene's own Show) |
| W3 | `apps/management-web/src/features/walls/WallForm.tsx:162` | Save (form submit) | `<Button type="submit" disabled={isLoading} busy={isLoading}>` | direct; **the exact #2624 form-submit shape** |

`disabled` in L1–L3 and O1–O5 is one per-row local, `publishing || archiving || branching || reverting`
(`LayoutsPage.tsx:161`, `OverlaysPage.tsx:156`).

### Drift from the issue's list

| Change | What | Why |
|---|---|---|
| **Added** | `walls/WallDetailPage.tsx` (W1, W2) and `walls/WallForm.tsx` (W3) | Spec 258 (walls) landed 2026-09-26, after spec 267 took its inventory at `b23b9307`. W3 is the identical `disabled={isLoading}` submit #2624 fixed five of. |
| **Restructured** | `LayoutsPage.tsx` | Spec 266 / #2335 (`69fa0e47`, 2026-09-27) moved Discard draft, Revert and Archive behind a `DropdownMenu`. Spec 267 listed five inline buttons (`:173,188,211,225,246`); today the native sites are Publish, Edit (new draft) and the menu trigger (L1–L3). |
| **Removed** | `OverlaysPage.tsx:209` Edit draft | Its handler sends nothing (it only opens the editor), so activating it never disables it. It fails ADR-0151 §1's test "decidable by reading the control's own handler". Judgement call [J1]. |
| **Removed** | `apps/shared/src/ui/composites/BackdropControls.tsx:79,182` | Fenced for **#2342** (open; "the overlay editor is 100% inline styles"). The file says so at `:29-31`, and spec 266 fenced it. `:79` (the Captured-frame radio) is not own-activation anyway. **Not touched.** |
| **Renamed path** | `features/systemVariables/`, not `features/system-variables/` | Path only. |

### Same-shape or near sites, reported and not in scope

| File:line | Control | Why not here |
|---|---|---|
| `apps/management-web/src/features/walls/WallForm.tsx:133,142` | scene Up / Down (plain `<button>`) | Up on the second scene makes that same node's Up `disabled` (index 0): ADR-0151's class. But Down very likely loses focus through a different mechanism, React **moving the focused `<li>`** during keyed reconciliation, which `aria-disabled` cannot fix. A half-fix is worse than a follow-up that owns reorder focus. **Recommend a follow-up issue** (mechanism unverified; to be observed there). |
| `LayoutsPage.tsx:172,187,203` | Menu entries' `disabled` | Radix `DropdownMenu.Item`'s own prop (`data-disabled`), not native `disabled`; the menu closes on select. These entries are L3's guard (plan §3.3), so they **stay**. |
| Publish, Discard draft, archived-variable row, `AuditPage.tsx:187` First page | Controls that **unmount** after success | Focus lost by removal, not by disabling. ADR-0151 does not cover it; it happens after the in-flight window this spec fixes. Recorded, not filed. |
| `apps/shared/src/ui/primitives/{Select,Tabs,DropdownMenu}.tsx` | Pass-through `disabled` props | Primitives forwarding a caller's prop. Not sites. |

## 2. User stories

Every story is the same defect. They are split **by file**, not by guard shape: each file's controls
share one guard shape and one mocked-mutation setup, so each story is one engineer's disjoint unit
(ADR-0109). All are P1, and each ships alone.

- **US1 (P1), System variables**: S1, S2. An operator who sets a value, or confirms an archive, keeps
  their place.
- **US2 (P1), Layouts row actions**: L1–L3, including Revert through the menu.
- **US3 (P1), Overlays row actions**: O1–O5.
- **US4 (P1), Camera pagination**: C1, C2.
- **US5 (P1), Audit pagination**: A1.
- **US6 (P1), Wall scene controls**: W1, W2.
- **US7 (P1), New wall submit**: W3. This is the only form-submit site, and so the only one that owes
  the implicit-submission proof.

## 3. Acceptance scenarios

Legend: *held*: the test holds the request at the route until it releases it.

### Click controls (S1, L1, L2, O1, O3, O4, C1, C2, W1, W2: direct)

```gherkin
Scenario: Focus survives activating the control (happy path)
  Given the control is available and its request will be held
  When the operator focuses it and presses Enter
  Then while held it is still document.activeElement
  And it has aria-disabled="true" and no disabled attribute

Scenario: A second activation while unavailable sends nothing
  Given the control is aria-disabled (held in flight, or terminally unavailable)
  When the operator presses Enter on it, or clicks it
  Then no request is sent, and no state changes (count stays at 1)

Scenario: A refused request leaves the operator on the control (conflict / bad request)
  Given the request is answered with the page's existing refusal (409 / 412 / 400)
  Then the refusal banner renders exactly as today
  And the control is focused and no longer aria-disabled, unless its terminal condition holds
```

### Focus-return controls (S2, O2, O5, and L3 via Discard draft / Archive)

```gherkin
Scenario: Focus returns to the opener while the archive is in flight
  Given the confirmation was opened from the control and the archive request will be held
  When the operator confirms
  Then while held the opener is document.activeElement with aria-disabled="true"
  And activating it again opens no second confirmation
```

### Menu trigger (L3, via Revert)

```gherkin
Scenario: Focus stays on More actions through a Revert
  Given a published layout row and the revert request will be held
  When the operator opens More actions and selects Revert with the keyboard
  Then while held More actions is document.activeElement with aria-disabled="true"
  And if the menu is reopened, every entry is disabled and selecting one sends nothing
```

### Terminal (A1, C2 last page, W2 shown scene, S1 after success)

```gherkin
Scenario: Reaching the last audit page keeps the operator on Next
  Given the next audit page is the last one (its nextCursor is null)
  When the operator presses Enter on Next
  Then after the page loads Next is document.activeElement with aria-disabled="true"
  And pressing Enter again changes nothing (it does not jump back to the first page)
```

The last line is a real consequence, not a restatement. `AuditPage.tsx:202` sets
`cursor: data?.nextCursor ?? undefined`, so an unguarded click on a null cursor would **reset to page
one**. Native `disabled` hides this today; the guard must keep hiding it.

### Form submit (W3)

```gherkin
Scenario: Focus survives an Enter-key Save, and implicit submission sends nothing more
  Given a valid name and two scenes, and the create request will be held
  When the operator focuses Save and presses Enter
  Then while held Save is document.activeElement with aria-disabled="true" and aria-busy="true"
  When the operator presses Enter in the Name field
  Then no second request is sent (count is 1 after the first settles)

Scenario: Validation failure is unchanged
  Given no name
  When the operator submits
  Then no request is sent and "name is required" shows, exactly as today
```

**Auth scenario: N/A, with the reason.** No endpoint, scope or policy changes. Every request is
unchanged in method, path, body and credentials, and the existing per-endpoint 401/403 coverage stands.

## 4. Requirements

- **FR-001**: Each of S1–S2, L1–L3, O1–O5, C1–C2, A1 and W1–W3 announces unavailability through the
  `Button` primitive's `unavailable` prop (ADR-0151 §3), never through native `disabled`. The prop's
  expression is **today's `disabled` expression, unchanged**.
- **FR-002**: Every converted control refuses its action while unavailable, with a guard that states
  the **same condition** as its `unavailable` expression:
  - Click controls: `if (<condition>) return;` at the top of the handler (ADR-0151's spec-156 shape).
  - W3: the form's `onSubmit`, before `handleSubmit`, as `handleFormSubmit` in
    `RegisterCameraDialog.tsx:107-113` does (spec-160 shape).
  - L3: **no new guard.** Every menu entry already carries Radix `disabled` on the same `disabled`
    local, which suppresses `onSelect` (spec-154 shape: the action already no-ops). See plan §3.3.
- **FR-003**: Outside the unavailable window every control behaves exactly as today: same labels, same
  requests, same banners. `busy` stays where it is (W3 only). No `busy`, and no
  `aria-disabled:cursor-progress` class, is added anywhere: `busy` adoption is spec 268's call.
- **FR-004**: Each **mechanism** has a real-browser Playwright proof that focus survives. W3 also has
  one of implicit submission, with Enter pressed **in the Name field** (ADR-0151 Implementation Notes).
  jsdom cannot observe the disable-blur.
- **FR-005**: Each converted control has a jsdom proof of the attribute (`aria-disabled="true"`, no
  `disabled`) and of its guard (activated while unavailable, the mutation or state setter is not called).

## 5. Assumptions and judgement calls (marked, not buried)

- **[J1] Overlays Edit draft (`:209`) stays native.** Its only way to disable is another row action's
  mutation, and while that is in flight focus is on that action, not on this one. The same reading
  admits Layouts Edit (new draft) (L2), because its handler **does** send `branchDraft`. A reviewer who
  prefers row uniformity flips `:209` with a trivial `if (disabled) return;`; nothing else depends on it.
- **[J2] L3's trigger still opens the menu while unavailable.** Radix `Trigger` reads its own
  `disabled` prop, not `aria-disabled`, so it opens. Every entry inside is disabled, so nothing can act.
  Suppressing the open would need `onPointerDown` / `onKeyDown` `preventDefault` on the trigger. That
  means new mechanism for no refused action, so it is rejected (ADR-0036).
- **[J3] S1's guard adds `raw === ''`.** Today an empty value can't be sent because native `disabled`
  blocks the click. The guard must keep that, or FR-003 breaks.
- **[J4] Focus-return correctness is inherited from `ConfirmDialog`** (`previouslyFocusedRef`, spec
  267). For L3 the opener is the trigger, because `DropdownMenu.tsx:59` focuses it before `onSelect`.
- **[J5] WallForm Up/Down are deferred** (§1): a follow-up issue, not this spec.

## 6. Independent end-to-end test procedure (Phase 5)

1. Boot the stack (`aspire run`), open management-web and sign in as the seeded operator.
2. Turn on DevTools network throttling (Slow 3G).
3. For each direct and focus-return control: reach it **by Tab** and press **Enter** (confirm where there
   is a dialog). During the request, run `document.activeElement` in the console. It must be the
   control, not `<body>`. Press **Tab** once: focus moves to the next control, not to the page's first
   link.
4. For Layouts: open More actions, then Revert, and check the same thing on the trigger.
5. For Audit, page to the last page with Enter on Next. Next stays focused and `aria-disabled`, and a
   further Enter does not return to the first page.
6. For New wall: during `Saving…`, press Enter in Name. The Network tab shows exactly one `POST`.
7. Automated equivalent: `pnpm test:e2e e2e/in-flight-focus.spec.ts` against the booted stack.

## 7. Locked tech choices (unchanged)

React + TypeScript + Vite (ADR-0074); RTK Query `isLoading` / `isFetching` as the in-flight signal
(ADR-0075); the `Button` primitive's `unavailable` (ADR-0077, ADR-0151); React Hook Form `handleSubmit`
(ADR-0079); Playwright against the live Aspire stack (ADR-0108); vitest + Testing Library in jsdom.

## 8. Phase 4a colour

**RED: behaviour-changing**, the same call as #2624. On `develop`, focus falls to `<body>` at every
site; afterwards it stays. The expected-red set and the declared green pins are listed per task in
`tasks.md`.
