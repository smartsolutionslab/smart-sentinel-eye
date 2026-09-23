# Spec 229 — The hub handlers CellPage carries

**Issue:** [#2321](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2321)
— "CellPage.tsx is 675 lines and growing — extract the hub message handlers".
Labelled `tech-debt`, `agent:ready`; Project #13, status In Progress.
**Branch:** `refactor/2321-extract-cellpage-hub-handlers`
**Created:** 2026-09-23
**Status:** Phases 1-3 complete — awaiting phase 4
**Lane:** autonomous (ADR-0144)
**Colour:** **behaviour-preserving — characterisation, observed green** (ADR-0144 phase 4a)

**ADRs this spec is bound by:**

- **ADR-0036** — smallest possible change; a refactor changes shape, not behaviour.
- **ADR-0144** — the characterisation path: covering tests captured passing
  *before* the change, and passing **unmodified** after it. A refactor with no
  covering test is a rewrite, so uncovered behaviour gets a test first.
- **ADR-0145** — the wall's fab is derived from its layout; the fab filter on the
  resolved-text and highlight routes is behaviour this spec must not move.
- **ADR-0112** §5 — highlight-all-matching, OR'd expiries; preserved verbatim.
- **ADR-0075** — RTK Query cache invalidation / `upsertQueryData` from the hub;
  preserved verbatim.

No ADR is written or amended. The extraction follows a shape the codebase
already has (`useWallAlignment`, spec 045), so there is no new decision to record.

---

## 0. Premise check (issue filed against an older file)

Verified against `b8c14eb9` on 2026-09-23.

| Issue says | Now | Consequence |
|---|---|---|
| `CellPage.tsx` is 675 lines | **707 lines** | Grew further (#2320's "two absent fabs" work). Premise holds, more strongly. |
| `CellPage.test.tsx` has 41 facts | **53 tests, 53 passed** (`npx vitest run src/features/cell/CellPage.test.tsx`, 14.1 s) | The covering suite is 53, not 41. |
| Handlers `onOverlayPublished`, `onResolvedOverlayTextChanged`, the highlight handler exist | All present, plus `onOverlayArchived` | `onOverlayArchived` moves with them — it shares `unavailableOverlays` with `onOverlayPublished`. |
| The three latch refs "they now share" | **Only two are shared by the handlers.** `staticLabelOverlaysRef` and `reportedStaticPushRef` are read by `onResolvedOverlayTextChanged`. `reportedLayoutFaultsRef` is read by **no hub handler** — only by the layout-fault `useEffect` (`CellPage.tsx:154-170`), which fires on the layout query, not on a hub frame. | `reportedLayoutFaultsRef` and its effect **stay in `CellPage`**. See §3, decision D1. |
| The existing suite covers the handlers | **Not all of them.** The suite fires `onOverlayHighlightChanged` (16 sites) and `onResolvedOverlayTextChanged` (3 direct sites plus helpers). It **never fires `onOverlayPublished` or `onOverlayArchived`**. The FR-009 test covers a *fetched* archived overlay, not a *pushed* one. | Two of the four moving handlers have no covering test. Per ADR-0144 they get characterisation tests **first** (US1), observed green on the unrefactored code. |

---

## 1. User stories

### US1 (P1) — The two uncovered handlers get a safety net before anything moves

*As the maintainer of the kiosk wall, I want the pushed overlay-archived and
overlay-published behaviour pinned by tests before the handlers move, so a
refactor that drops or rewires them is caught rather than shipped.*

**Why P1:** without it, US2 is a rewrite of two handlers with no evidence of
equivalence. This is the ADR-0144 precondition for the characterisation colour.

**Acceptance scenarios** (all observed **green** against the *unrefactored*
`CellPage.tsx`; each proven able to fail by counterfactual):

```gherkin
Scenario: happy — a pushed archive flags the bound tile
  Given a wall whose tile binds overlay "ov-1" with a Published revision
  When the hub delivers OverlayRevisionArchived for "ov-1"
  Then that tile shows "Overlay unavailable"
  And a tile bound to a different overlay does not

Scenario: happy — a later publish clears the flag
  Given the tile bound to "ov-1" is flagged unavailable by a pushed archive
  When the hub delivers OverlayRevisionPublished for "ov-1"
  Then the tile no longer shows "Overlay unavailable"

Scenario: happy — a publish re-reads the bound overlay
  Given a wall whose tile binds "ov-1"
  When the hub delivers OverlayRevisionPublished for "ov-1"
  Then the "Overlay" and "OverlaySnapshot" cache entries for "ov-1" are invalidated
  (observed at the store: `useGetOverlayQuery` is mocked in this suite, so no
  re-fetch is visible — spy on the real `store.dispatch` before render and match
  the dispatched actions with `overlaysApi.util.invalidateTags.match` /
  `systemVariablesApi.util.invalidateTags.match`, asserting the tag payloads)

Scenario: bad input — a frame for an unbound overlay is a no-op
  Given a wall that binds only "ov-1"
  When the hub delivers OverlayRevisionArchived, then OverlayRevisionPublished, for "ov-9"
  Then no tile is flagged and nothing is invalidated
```

There is no auth or conflict scenario: these handlers sit behind the hub
subscription (`enabled: auth.isAuthenticated`), which is unchanged and already
covered by `useLayoutLifecycle.test.tsx`; and they carry no version, so there is
nothing to conflict on. Recorded rather than silently omitted.

### US2 (P1) — CellPage's overlay hub handling lives in its own hook

*As the maintainer of the kiosk wall, I want the overlay-scoped hub handlers and
the state they own in a dedicated hook beside `useWallAlignment`, so `CellPage`
reads as layout + grid + wiring and each concern can be read on its own.*

**Acceptance scenarios:**

```gherkin
Scenario: happy — the whole covering suite passes unmodified
  Given the 53 pre-existing CellPage tests and the US1 characterisation tests
  When the handlers are moved into useOverlayHubHandlers
  Then every one of them passes
  And `git diff <US1 commit>..HEAD -- apps/kiosk-web/src/features/cell/CellPage.test.tsx` is empty

Scenario: conflict — an assertion that needs editing is a stop, not a fix
  Given the extraction is in progress
  When any covering test fails after the move
  Then the engineer reverts the move of that piece and reports the failure verbatim
  And no test is edited to pass (ADR-0144: an edited assertion is evidence the behaviour moved)

Scenario: happy — CellPage is materially smaller
  When the extraction lands
  Then CellPage.tsx is under 500 lines (expected ≈ 450)
  And no hub-frame handler body remains in it

Scenario: gates — nothing is weakened to get there
  Then kiosk-web lint (--max-warnings 0), typecheck and the full vitest run are clean
  And no new eslint-disable is added (the existing react-hooks/purity one moves with startHighlight)
```

---

## 2. Independent end-to-end test procedure

1. `cd apps/kiosk-web && npx vitest run src/features/cell/CellPage.test.tsx` on the
   US1 commit — record `Tests N passed (N)` verbatim (N = 53 + the US1 additions).
2. Apply the extraction commit.
3. Re-run the same command — the identical `N passed (N)` line, verbatim.
4. `git diff <US1 commit>..HEAD -- apps/kiosk-web/src/features/cell/CellPage.test.tsx`
   — empty output, quoted.
5. `npm run lint && npm run typecheck && npm test` in `apps/kiosk-web` — clean.
6. `wc -l` on `CellPage.tsx` and the new hook — before/after figures in the PR.

No stack boot is required to observe this change: it moves code within one
component tree and the hub is already mocked at its transport
(`@smart-sentinel-eye/shared/realtime/layoutHub`), below the moved code. Phase 5
may additionally run the kiosk wall e2e if a stack is up; it is not the evidence.

---

## 3. Decisions and assumptions

- **D1 — `reportedLayoutFaultsRef` and the layout-fault effect stay in `CellPage`.**
  The issue groups it with the handler latches; the code does not. It is written
  by an effect keyed on `[layoutIdentifier, published, wallFab]`, never by a hub
  frame, and its `published === undefined` early return is what keeps FR-004
  (spec 141) silent while the layout loads — a coupling its own comment warns a
  future split to keep. Moving it into a hook named for hub handling would be the
  wrong home and gains nothing. Divergence from the issue text, stated here and in
  the PR.
- **D2 — `onArchived` (layout) and `onReconnected` stay in `CellPage`.** They act
  on the page — `navigate` and the layout query's `refetch` — not on overlay state.
- **D3 — the hook does not own the hub subscription.** `CellPage` keeps calling
  `useLayoutLifecycle` and spreads the hook's handlers into it. Moving the
  subscription would be a second change (it would drag `onArchived`,
  `onReconnected`, `degraded` and the auth wiring with it).
- **A1 (verified, not assumed)** — `useLayoutLifecycle` reads the *latest*
  options through a ref updated every render (`useLayoutLifecycle.ts`, "Keep the
  latest options in a ref"), so handlers returned fresh from a hook each render
  behave exactly as the inline closures do today. The hook must **not** memoise
  them (a `useCallback` over stale `boundOverlays`/`wallFab` would be a behaviour
  change).

## 4. Latency budget impact

**Leg: Event → overlay state (≤ 200 ms).** `onResolvedOverlayTextChanged` and
`onOverlayHighlightChanged` are that leg's kiosk-side code. The refactor performs
the same operations in the same order, plus one hook call per render and no
extra render (state stays the same two `useState`s, now declared in the hook).
**No budget impact; no measurement required.** Not re-measured, and recorded as
such rather than claimed as measured.

## 5. Out of scope

- Any behaviour change, including fixing anything noticed while moving code
  (e.g. `countReportableSkew`'s deliberate `wallFab === undefined` blind spot —
  spec 141 SC-6). A fix is a separate issue.
- Unit tests for the new hook in isolation — the covering suite is at the page
  level on purpose, so it survives the move.
- `Tile`, `buildGridCells`, `EmptyCell`, `FullScreen` — untouched.
- An ESLint `max-lines` rule (spec 141 R6 records that none exists).
