# Spec 429 — The retry that keeps its place: `RetryBanner` focus and announcements

**Issue:** [#3073](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3073) (feature-level;
no per-task issues, per CLAUDE.md Phase 3). Split from #2378 (owner decision, 2026-10-21).
**Branch:** `fix/3073-retrybanner-focus` (cut from `develop` `6206ecf9`, which carries spec 427 / #2668:
`RetryBanner`'s Retry is already the shared `LinkButton` with the design-system focus ring).
**Lane:** autonomous (ADR-0144). **Not security-sensitive** (no endpoint, token, scope or input path).
**§IV latency leg: none** — management console only; nothing on the event-to-overlay path.

**Rule implemented (owner, #3073 body, confirmed here in phase 1).** Extend `RetryBanner` to follow the
`ChainRecoveryNotice` pattern: `aria-disabled` rather than `disabled` while the retry is in flight, an
always-mounted live region whose text changes, and focus moved to the page `<h1>` when a retry the
operator started succeeds. **No ADR**: the pattern already exists in this repo
(`apps/shared/src/ui/composites/ChainRecoveryNotice.tsx`, spec 156 / #2372), and ADR-0151 already decides
`aria-disabled` over native `disabled`. The focus target (`<h1>`) is the owner's stated rule, not an
architecture call — unlike #2378's CrashPanel, where two precedents conflict *across a navigation*.

**ADRs this spec is bound by:** ADR-0077 (design-system composites in `apps/shared/src/ui`), ADR-0151
(`aria-disabled` keeps focus), ADR-0075 (RTK Query is the fetch state the banner reads), ADR-0036 (no
speculative generality), ADR-0139 / ADR-0144 (phase-4a colours, the lane), ADR-0087 (every commit builds
on its own). **Predecessors:** spec 156 (#2372, `ChainRecoveryNotice`), #2346 (a live region inserted
with its content is not reliably announced), #2344 (an unchanged `setState` string is a no-op), spec 298
(#2693, `RetryBanner` draws through `FaultNotice`; RulesPage adopted it), spec 427 (#2668, `LinkButton`).

---

## 1. The premise, re-verified on `6206ecf9` (2026-10-21)

`RetryBanner` today (`apps/shared/src/ui/composites/RetryBanner.tsx:12-17`) is stateless: a
`FaultNotice` (`FaultNotice.tsx:9-10`, `role="alert"`) holding the message and a `LinkButton` Retry. No
in-flight state, no live region, no focus handling. Spec 427 changed only its styling.

**Sites (10, not 8).** `grep -rn "<RetryBanner" apps --include=*.tsx` (tests excluded):

| # | Site | Mount condition | `<h1>` |
|---|---|---|---|
| S1 | `management-web/.../systemVariables/SystemVariablesPage.tsx:131` | `error !== undefined &&` | `:119` |
| S2 | `management-web/.../layouts/LayoutsPage.tsx:140` | `error !== undefined &&` | `:128` |
| S3 | `management-web/.../overlays/OverlaysPage.tsx:128` | `error !== undefined &&` | `:116` |
| S4 | `management-web/.../walls/WallsPage.tsx:35` | `error !== undefined &&` | `:31` |
| S5 | `management-web/.../audit/AuditPage.tsx:211` | `error !== undefined &&` | `:166` |
| S6 | `management-cameras/.../cameras/CamerasPage.tsx:163` | `error !== undefined &&` | `:159` |
| S7 | `management-cameras/.../cameras/CameraDetailPage.tsx:163-168` | `error !== undefined &&` | `:132` |
| S8 | `management-web/.../rules/RulesPage.tsx:188-189` | `isError ? banner : table` | `:156` |
| S9 | `management-event-ingestion/.../eventTypes/HeldEventTypesPage.tsx:125-126` | `error !== undefined ? banner : content` | `:119` |
| S10 | `management-web/.../overlays/OverlayEditPage.tsx:135-144` | whole-tree branch on `chainFailed` (`isError`) | in `PageHeader` |

S9 and S10 post-date #2378's list; S8 adopted `RetryBanner` in spec 298.

**Per defect named in #2378 / #3073:**

| Defect | At HEAD? | Evidence |
|---|---|---|
| D1. Focus falls to `<body>` when Retry succeeds | **Still exists, S1-S9** | On success RTK Query clears `error`, the site's condition unmounts `RetryBanner`, and the focused Retry goes with it. Nothing anywhere focuses a heading: `grep -rn "tabIndex={-1}" apps --include=*.tsx` is empty. |
| D2. A repeat failure is announced silently | **Still exists, S1-S9** | `error` stays defined through the refetch (`CameraDetailPage.tsx:92-93`: "a refusal keeps `error`"; #2378's RTK Query source reading), so the `role="alert"` `<div>` is the same node before and after the second refusal, with the same text. No insertion, no text change: nothing for a screen reader to announce (#2346). |
| D3. Retry is natively-unguarded while in flight | **Still exists, S1-S10** | `RetryBanner.tsx:15` passes neither `aria-disabled` nor a guard; a second click during the in-flight window calls `refetch()` again, and nothing tells the operator a retry is running. |
| D4. Retry destroys itself on click | **Exists at S8 only (in scope), and S10 (out of scope, §5)** | `RulesPage.tsx:188` gates on `isError`, which RTK Query sets false the instant `refetch()` goes `pending` — before the response exists (`ChainRecoveryNotice.tsx:33-37` records the same). S1-S7 and S9 gate on `error`, which survives the refetch — the premise #3073 asked to re-verify holds for them. |
| D5. Stale conflict state after Reload | **Out of scope** | The four conflict Reload controls stay on #2378 (owner decision). |

**Premise: partly holds.** The issue's rule is right for S1-S9. S10 needs a structural change to a page
with heavy seeding invariants (`OverlayEditPage.tsx:82-110`, `:146-178`) and is split out (§5).

## 2. User stories

### US1 (P1) — Retry on a list page keeps the operator's place

As an operator using a keyboard or screen reader on a console page whose load failed, when I activate
Retry I want to hear that a retry is running, hear a second failure if it fails again, and land on the
page heading when it succeeds — so I never lose my place at `<body>`.

Sites: shared `RetryBanner` + S1-S7 (the seven `error !== undefined &&` sites — additive wiring only).

### US2 (P2) — The two sites whose banner swaps with the content

Same outcome at S8 (RulesPage, which today destroys Retry on click) and S9 (HeldEventTypesPage, a
banner/content ternary). Separate story because both need their render shape changed so the banner's
live region stays mounted while the content swaps.

## 3. Acceptance scenarios

```gherkin
Feature: RetryBanner keeps focus and announcements correct

  Background:
    Given an operator is on a console page whose list read was refused
    And the page shows "Could not load <things>." with a Retry control

  # happy path
  Scenario: A retry the operator started succeeds
    When the operator activates Retry
    Then the live region says "Retrying…"
    And Retry stays mounted, focused, and aria-disabled="true" while the read is in flight
    When the read succeeds
    Then the fault notice is gone
    And the live region says "Loaded."
    And focus is on the page's <h1>

  # conflict: the read is refused again
  Scenario: A retry the operator started fails again
    When the operator activates Retry
    And the read is refused again
    Then the fault notice is a newly inserted element (a fresh alert announcement)
    And focus is on Retry
    And the live region is empty

  # bad request: activation while a read is already in flight
  Scenario: Retry is activated while a read is in flight
    Given a read of the list is in flight
    Then Retry carries aria-disabled="true" and is not natively disabled
    When the operator activates Retry
    Then no second read is started

  # unrequested read (refetchOnFocus, a mutation's own refetch)
  Scenario: A read nobody asked for heals the page
    Given the operator did not activate Retry
    When a background read succeeds
    Then focus does not move
    And the live region says nothing

  Scenario: A read nobody asked for is refused again
    Given the operator did not activate Retry
    When a background read is refused again
    Then the fault notice is not re-inserted and focus does not move

  # auth: a refusal is a refusal
  Scenario: The retried read is answered 403
    When the operator activates Retry and the read is answered 403
    Then it behaves as "A retry the operator started fails again"
```

Auth note: no new scope or endpoint. Revocation handling (`useRevocationFallback`, spec 310) is
untouched; it decides what the page renders, `RetryBanner` only how the banner behaves within it.

## 4. Functional requirements

- **FR-001** `RetryBanner` renders an always-mounted, visually hidden live region (`aria-live="polite"`,
  `data-testid="retry-banner-live-region"`), present whether or not the load has failed.
- **FR-002** The fault notice (`FaultNotice`, `role="alert"`, message + Retry) renders only while
  `failed` is true.
- **FR-003** Retry carries `aria-disabled` equal to `retrying`, never native `disabled` (ADR-0151); while
  `retrying` is true, activation calls nothing.
- **FR-004** Activating Retry writes "Retrying…" to the live region (with a token, so a repeat is never
  the same-string no-op of #2344) and calls `onRetry`.
- **FR-005** When a read **started by Retry** settles with `failed` false: live region "Loaded.",
  `onRecovered()` called exactly once.
- **FR-006** When a read **started by Retry** settles with `failed` still true: live region cleared, the
  fault notice remounted (a new element, so the alert is re-inserted), focus restored to the new Retry.
- **FR-007** A read **not** started by Retry changes nothing that is announced or focused: no live-region
  text, no remount, no `onRecovered()`. (Today's behaviour for those reads, kept.)
- **FR-008** Every in-scope site (S1-S9) mounts `RetryBanner` unconditionally with `failed` = the query's
  `error !== undefined`, `retrying` = its `isFetching`, and `onRecovered` focusing the page `<h1>`, which
  carries `tabIndex={-1}` (focusable by script, not in the tab order).
- **FR-009** Defaults keep an un-migrated call site behaving exactly as today: `failed` defaults `true`,
  `retrying` defaults `false`, `onRecovered` is optional. (S10 relies on this; §5.)
- **FR-010** S8 gates on `error`, not `isError`, so Retry survives its own click (D4).

## 5. Out of scope

- **S10 `OverlayEditPage`** — its banner lives in a whole-tree branch (`OverlayEditPage.tsx:135-144`) that
  is replaced by the read-only placeholder form on click and by the seeded form on success, so neither
  the live region nor the recovery edge can survive there without restructuring the seeding branches.
  Unchanged by this spec (FR-009). **Follow-up issue to file** (text in plan §8).
- Conflict Reload ×4, `ShellLayout` CrashPanel, kiosk `ReconnectingScreen` — stay on #2378.
- Extracting a hook shared with `ChainRecoveryNotice` — a refactor; mixing it in would make this a
  behaviour change and a refactor at once (ADR-0036 / CLAUDE.md "smallest change").
- Any lint/arch guard for "focusable control in a conditional live region" (#2378 "Deliberately not a
  lint rule — yet"; S10 and the #2378 sites still exist).

## 6. Decisions and assumptions (surface in the PR body)

- **D1 `aria-live="polite"` instead of `role="status"`.** Same announcement semantics; chosen because an
  always-mounted `role="status"` collides with five existing unscoped assertions
  (`CameraDetailPage.test.tsx:180,181,361,614` `getByRole('status')`, `HeldEventTypesPage.test.tsx:173`
  `queryByRole('status')…not.toBeInTheDocument()`), which would have to be edited. Precedent:
  `OverlayEditor.tsx:696,739` (`aria-live="polite"`, sr-only, `data-testid`).
- **A1 (guess)** The success text is the generic "Loaded." — the focused `<h1>` already names the page.
- **A2 (guess)** The focused `<h1>` shows no focus ring (`focus:outline-none`): it is not a control, and
  a ring on a heading reads as "clickable".
- **A3** `retrying` = `isFetching`, i.e. Retry is unavailable during *any* read of that query, including
  an Audit/Cameras page change — the same choice `ChainRecoveryNotice`'s `reReading` makes.

## 7. Independent end-to-end test procedure (phase 5)

1. `aspire run`; sign in to management-web as an operator; open **Walls**.
2. Stop the Walls service resource in the Aspire dashboard; reload the page → "Could not load walls.".
3. With a screen reader (NVDA) on, Tab to Retry, press Enter → hear "Retrying…", then the alert again;
   focus is still on Retry.
4. Start the resource; press Enter on Retry → hear "Loaded." / "Walls, heading level 1"; `document.activeElement`
   is the `<h1>` (DevTools console).
5. Repeat 2-4 on **Rules** (S8) and confirm Retry is not removed on the click.
