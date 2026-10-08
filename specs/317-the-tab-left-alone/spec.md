# Spec 317 — The tab left alone

**Issue:** [#2751](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2751) — the
spec-310 revocation fallback only strikes on a refresh, and the six guarded pages never refresh on
their own, so a tab left untouched keeps a revoked record on screen indefinitely.
**Branch:** `feat/2751-focus-refresh-stale-pages` (cut from `origin/develop` @ `ba37ca5a`)
**Created:** 2026-10-08
**ADRs:** 0075 (Redux Toolkit + RTK Query), 0113 (two-layer optimistic concurrency — why one page
suspends the refresh, §1 D2), 0139 (new behaviour starts red), 0150 (waiting is a condition, not a
count), 0109 (disjoint files), 0037 (phased workflow).
**Closes the follow-up named in:** spec 310 §1 item 2 ("a passive tab never refreshes … recommend a
follow-up issue").
**Composes with:** spec 314 / #2762 (PR #2776, merged) — the listener-based strike counting. This
spec adds a new *source* of refreshes; it does not touch how a refresh is counted. See plan §2.

**Decision already made (user, 2026-10-08):** refresh on focus — not polling, not accepted
staleness. This spec implements that decision; it does not reopen it.

**No ADR.** RTK Query's documented, built-in `setupListeners` + per-subscription `refetchOnFocus`
inside the locked RTK decision (ADR-0075). No new package, runtime resource or pattern.

**Spec number.** 2026-10-08: `origin/develop` tops out at **315**; the unmerged branch
`feat/316-operator-mfe-shell-and-first-remote` claims **316** (ADR-0168 / spec 316). So **317**.
Re-check before opening the PR (memory: *spec number: origin/develop isn't enough*).

## 0. The gap (verified against the code and RTK 2.12.0 source, not the issue text)

- `setupListeners` is called nowhere in `apps/` (grep: zero production hits; the only mention is a
  comment in `CameraDetailRevocation.test.tsx:96` stating exactly that). Without it RTK Query never
  receives a focus signal at all.
- None of the six guarded query hooks passes `refetchOnFocus`, and no `createApi` sets it, so even
  with listeners installed nothing would refetch (`windowEventHandling.ts`: a cache entry is
  refetched on focus only if one of its subscriptions says `refetchOnFocus: true`, or all are
  silent and the api-level default is true — it is false everywhere).
- Today's only refresh sources for these six queries: Retry, tag invalidation after the operator's
  own mutation, remount after the 60 s `keepUnusedDataFor`. A mounted page that is not touched
  never refetches, so it never strikes.

## 1. Decisions taken in this spec

D2 confirmed by the coordinator at the Phase-1 gate, 2026-10-08.

- **D1 — Per-hook opt-in, not a global default.** `refetchOnFocus: true` on exactly the six
  guarded queries. A global default (on the eight `createApi` slices in `apps/shared`, which
  kiosk-web also imports) would refetch the editors' own queries mid-edit
  (`useGetLayoutQuery` in `LayoutEditorDialog`, `useGetOverlayQuery` in `OverlayDraftForm` /
  `OverlayEditPage`) and every other query in the app on every alt-tab, for no requirement anyone
  has stated. The issue names six pages; the change names six call sites.
- **D2 — `CameraDetailPage` suspends its focus refresh while one of its three dialogs is open.**
  It is the only one of the six pages that hands a **live** query field to an open editor:
  `RenameCameraDialog` and `EditCameraAddressDialog` receive `version={record.version}` (echoed as
  `If-Match`, ADR-0113) and `currentName`/`currentUrl` straight from the query result, behind a
  modal that hides the record. Today nothing refetches `getCamera` while a dialog is open, so that
  version is always the one the operator was shown. A focus refresh would become the first path
  that silently advances it under the open dialog — turning a concurrent edit the operator never
  saw into a successful overwrite instead of the `412` ADR-0113 exists to produce.
  The five list pages are safe as they are: confirm dialogs snapshot the version into state at
  open (`archiveFor`, `discardFor`, `editTarget`), and row actions read the version at click time
  from the row on screen. `SystemVariablesPage`'s inline "Set value" reads the live row's version
  at click, but the refreshed `Current:` value renders beside the input, unobscured — the operator
  is shown the version they quote. **Accepted as is.**
  *Alternative rejected:* snapshot `version` at dialog open in `CameraDetailPage`. That changes
  existing behaviour for paths that already exist (a different change with its own justification);
  suspending only governs the new behaviour this spec adds (ADR-0036, smallest change).
- **D3 — The window listeners are installed by the app bootstrap, not as a module side effect of
  `store.ts`.** ~30 test files import the real `store`; a module-level `setupListeners` would attach
  `window` listeners in every one of them, and RTK's `setupListeners` keeps a **module-global**
  `initialized` flag, so any such file that then calls `setupListeners` for its own store would get
  a silent no-op. `store.ts` exports one function that installs them for the app store;
  `main.tsx` calls it once before rendering.

## 2. User stories

### US1 (P1) — A passive operator's revoked record leaves the screen when they come back to the tab

As a shift supervisor, when an operator's access is revoked while one of their management tabs
sits in the background, I need the record to go stale the way spec 310 intends as soon as they
return to that tab — three returns, three refusals, record gone — without them having to click
Retry, so that a tab left open overnight cannot keep showing a camera, layout, overlay, variable or
audit trail its operator may no longer see.

**Why P1 / the whole slice:** it is the issue. One bootstrap call, one option at six call sites
(one of them conditional). Observable end to end through the existing spec-310 refusal surfaces.
There is no smaller shippable vertical: the bootstrap call alone changes nothing, and the option
alone is inert without it.

### Acceptance scenarios (Gherkin)

```gherkin
Feature: The six revocation-guarded pages refresh when the window regains focus

  Background:
    Given the management app's store with window focus listeners installed
    And the network is stubbed per request

  # Happy path — the refresh itself
  Scenario Outline: Returning to the tab refetches the page's guarded query
    Given <page> has loaded and rendered its data
    When the window regains focus
    Then exactly one new request for <page>'s guarded query is sent
    Examples:
      | page                |
      | CameraDetailPage    |
      | CamerasPage         |
      | AuditPage           |
      | LayoutsPage         |
      | OverlaysPage        |
      | SystemVariablesPage |

  Scenario: A tab becoming visible counts as regaining focus
    Given CamerasPage has loaded and rendered its data
    When the document's visibility changes to "visible"
    Then exactly one new request for the camera list is sent

  # Happy path — the issue's outcome, through spec 310's surface
  Scenario Outline: Three focus refreshes refused take the data off screen
    Given <page> has loaded and rendered its data
    When the window regains focus three times and each refresh answers 403
    Then <page> shows its spec-310 refusal surface instead of the data
    Examples:
      | page                |
      | CameraDetailPage    |
      | CamerasPage         |
      | AuditPage           |
      | LayoutsPage         |
      | OverlaysPage        |
      | SystemVariablesPage |

  # Conflict — overlapping signals and in-flight refreshes
  Scenario: Focus and visibility arriving together send one request, count one strike
    Given a page whose guarded query has loaded
    When "focus" and "visibilitychange" to visible fire back to back
    Then exactly one request is sent
    And at most one strike is counted for it

  Scenario: A focus refresh coalesced with the next one by autobatch is still counted
    Given the hook's store holds autobatch notifications until released
    When three focus refreshes each answer 403 before any notification is released
    Then the hook reports refused once notifications are released

  Scenario: A successful focus refresh between refusals resets the count
    Given two focus refreshes answered 403
    When the next focus refresh answers 200
    And two more focus refreshes answer 403
    Then the refusal surface is not shown

  # Conflict — the open editor (D2)
  Scenario Outline: CameraDetailPage does not refresh behind an open dialog
    Given CameraDetailPage has loaded camera version 7
    And the <dialog> dialog is open
    When the window regains focus
    Then no request for the camera is sent
    When the dialog is closed and the window regains focus
    Then exactly one request for the camera is sent
    Examples:
      | dialog              |
      | Rename              |
      | Edit camera address |
      | Retire              |

  # Bad request — the app outside the six pages
  Scenario: Queries that did not opt in are not refetched on focus
    Given a query subscription without refetchOnFocus is active in the app store
    When the window regains focus
    Then no request for it is sent

  Scenario: Installing the listeners twice does not double the refreshes
    Given the app's focus listeners have been installed
    When they are installed a second time
    And the window regains focus
    Then exactly one request per opted-in query is sent

  # Auth — semantics unchanged (spec 310 / 313 / 314)
  Scenario Outline: A focus refresh is classified like any other refresh
    Given CameraDetailPage holds a loaded record
    When three focus refreshes each answer <status>
    Then the refusal surface shown is <refused>
    Examples:
      | status | refused |
      | 403    | true    |
      | 404    | true    |
      | 503    | false   |
      | 200    | false   |
```

## 3. Functional requirements

- **FR-001** The management app MUST install RTK Query's window focus/visibility listeners
  (`setupListeners`) against its store exactly once, from the app bootstrap (`main.tsx`), via a
  function exported by `app/store.ts` (D3). `store.ts` MUST NOT install them at module load.
- **FR-002** Each of the six spec-310 guarded queries MUST subscribe with `refetchOnFocus: true`:
  `useGetCameraQuery` (`CameraDetailPage`), `useListCamerasQuery` (`CamerasPage`),
  `useSearchAuditQuery` (`AuditPage`), `useListLayoutsQuery` (`LayoutsPage`), `useListOverlaysQuery`
  (`OverlaysPage`), `useListVariablesQuery` (`SystemVariablesPage`).
- **FR-003** `CameraDetailPage`'s `refetchOnFocus` MUST be false while any of its Rename, Edit
  camera address or Retire dialogs is open, and true otherwise (D2).
- **FR-004** No other query, and no `createApi` default, changes. Specifically: no api-level
  `refetchOnFocus`/`refetchOnReconnect`; `CamerasPage`'s `useListStreamsQuery` poll and
  `CameraViewer`'s poll keep their current behaviour (`skipPollingIfUnfocused` stays at its default
  `false`, so the focus-lost signal `setupListeners` now emits does not pause them).
- **FR-005** A focus-triggered refresh's settlement MUST be counted by `useRevocationFallback`
  through its existing listener path (spec 314 FR-001..FR-003) — no change to the hook, its
  signature, its threshold or its classification.
- **FR-006** The six page-level behaviours MUST be proven against the real RTK Query endpoint and a
  real store with `fetch` stubbed at the network boundary, driven by a real `window` event — not by
  asserting the option a mocked hook received.
- **FR-007** No test may wait on a fixed tick count or real timer for a refresh to settle
  (ADR-0150); settlement is awaited on a store condition (`getRunningQueriesThunk`, or the
  endpoint selector leaving `pending`).

## 4. Out of scope

- Polling, and any change to what a refresh's outcome means (spec 310/313/314 own that).
- Snapshotting the editor version at dialog open in `CameraDetailPage` (D2, alternative rejected).
  If wanted, it is its own issue.
- kiosk-web — it does not use the hook and does not get the listeners.
- Token renewal. A revoked scope is refused only once the access token carrying it is renewed
  (spec 310 §1, "also assumed") — focus refresh makes refusals *observable* sooner, not *issued*
  sooner.
- `refetchOnReconnect`. `setupListeners` also emits online/offline signals; nothing opts in to them
  and this spec does not.

## 5. Locked tech choices

React 19 + Redux Toolkit 2.12.0 (`setupListeners` from `@reduxjs/toolkit/query`, per-subscription
`refetchOnFocus`) + react-redux 9.3.0 — ADR-0075. Vitest + Testing Library + jsdom. No new
dependency.

## 6. Latency budget impact

**N/A.** management-web's revocation fallback is not on the event→overlay path (constitution §IV).
Server load: at most one GET per focus per mounted guarded page (one page is mounted at a time in
the management console; a focus+visibility pair is deduplicated by RTK's in-flight condition,
plan §2.3).

## 7. Independent end-to-end test procedure

1. **Deterministic (the evidence):**
   `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/app/focusRefetch.test.ts src/features/**/*FocusRefetch.test.tsx`
   and
   `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/hooks/useRevocationFallbackFocus.test.tsx`.
   Before the fix: the store-wiring test and every page's "focus refetches" and "three refusals"
   cases are red; the shared composition cases are green fences (they exercise RTK + the
   post-#2762 hook, which already compose — plan §2). After: all green.
2. **Full suites:** `apps/shared` and `apps/management-web` Vitest, twice (memory: measurement runs
   need repeating), plus `tsc --noEmit`, eslint and `prettier --check` for both.
3. **Live (phase 5):** on the Aspire stack, open each of the six pages in a tab; switch away and
   back, confirm in the browser's network panel exactly **one** request for the page's guarded
   query per return (not two — focus and visibility both fire). Open the Rename dialog on a camera
   detail page, switch away and back: **no** request. Then revoke the operator's read scope in
   Keycloak, wait for token renewal, return to the tab three times: the page shows its refusal
   surface.
