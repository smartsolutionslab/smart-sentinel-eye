# Plan 429 — The retry that keeps its place

**Spec:** [spec.md](./spec.md) · **Issue:** #3073

## 1. Constitution and ADR check

- Frontend only; no bounded context, no domain model, no `Shared.Contracts` change, no AppHost resource.
  §II (primitives on domain models), §IV (latency) and the cross-context rule are not engaged.
- ADR-0077: the change lives in the shared composite (`apps/shared/src/ui/composites/RetryBanner.tsx`)
  and its call sites. ADR-0151: `aria-disabled`, never native `disabled`. ADR-0075: the banner reads RTK
  Query state passed as props; it does not call the store. ADR-0036: no new primitive, no hook extracted.
- No ADR needed (spec header). The lane may proceed.

## 2. Layers touched

| Layer | Files |
|---|---|
| Shared composite | `apps/shared/src/ui/composites/RetryBanner.tsx` (+ `.test.tsx`) |
| management-web pages | `SystemVariablesPage.tsx`, `LayoutsPage.tsx`, `OverlaysPage.tsx`, `WallsPage.tsx`, `AuditPage.tsx`, `RulesPage.tsx` |
| management-cameras pages | `CamerasPage.tsx`, `CameraDetailPage.tsx` |
| management-event-ingestion | `HeldEventTypesPage.tsx` |
| Untouched | `OverlayEditPage.tsx` (FR-009 defaults), `FaultNotice.tsx`, `LinkButton.tsx`, `ChainRecoveryNotice.tsx` |

## 3. Component design

```ts
export interface RetryBannerProps {
  message: string;
  onRetry: () => void;
  /** The query's `error !== undefined`. Default `true` (FR-009). */
  failed?: boolean;
  /** The query's `isFetching`. Default `false`. */
  retrying?: boolean;
  /** Called once when a read started by Retry succeeds. */
  onRecovered?: () => void;
}
```

State, a trimmed port of `ChainRecoveryNotice` (one origin, not two — no Reload arm):

- `announcement: { text, token }` — rendered as `<span key={token}>{text}</span>` inside
  `<div aria-live="polite" className="sr-only" data-testid="retry-banner-live-region">` (OverlayEditor
  precedent, spec D1). Always mounted (FR-001).
- `failureKey: number` — the `key` on the fault-notice wrapper; bumped only on FR-006, so the first
  click → in-flight transition reuses the same node (Retry keeps focus), and a repeat refusal remounts it.
- `activatedRef` (set by click) → consumed on the `retrying` rising edge into `currentFetchStartedByRetryRef`
  → consumed on the falling edge. Per-fetch, never latched, so an unrequested read (refetchOnFocus, the
  promote refetch at `HeldEventTypesPage.tsx:80`) is never mistaken for the click (FR-007; the exact
  #2387 trap `ChainRecoveryNotice.tsx:90-110` documents).
- `wasRetryingRef` for edge detection; one `useEffect([retrying, failed])` for the edges, one
  `useEffect([failureKey])` (skipping first run) to refocus Retry after a remount.
- Retry: `<LinkButton ref={retryRef} aria-disabled={retrying} className="aria-disabled:cursor-progress"
  onClick={activate}>`; `activate` returns early while `retrying` (FR-003).

`FaultNotice` keeps owning the box and `role="alert"`; the `key` goes on `FaultNotice` itself.

**Per site (FR-008):** `const headingRef = useRef<HTMLHeadingElement>(null);` on the `<h1>` with
`tabIndex={-1}` and `focus:outline-none` (A2); `<RetryBanner failed={error !== undefined}
retrying={isFetching} onRecovered={() => headingRef.current?.focus()} message=… onRetry=… />`, mounted
unconditionally. `isFetching` is newly destructured at S4 (Walls), S8 (Rules), S9 (HeldEventTypes);
S1-S3, S5-S7 already have it. S8 also destructures `error` and drops `isError` (FR-010).

**Render reshaping:** S8 `isError ? <RetryBanner/> : <DataTable/>` → `<RetryBanner …/>` then
`{error === undefined && <DataTable/>}`. S9 `error !== undefined ? <RetryBanner/> : <>…</>` → banner then
`{error === undefined && <>…</>}`. Visible output identical in every state.

## 4. Messaging

None. No domain or integration event; no HTTP change.

## 5. Tests

### 5a. Red (observed failing before the source change; verbatim in the PR)

`apps/shared/src/ui/composites/RetryBanner.test.tsx` — new `describe('RetryBanner — recovery (spec 429)')`,
driving `failed`/`retrying` through `rerender` (no store):

- **R1** With `failed={false}`: no `alert`, but `getByTestId('retry-banner-live-region')` exists, empty.
- **R2** Click Retry, rerender `retrying` true: live region "Retrying…"; Retry has `aria-disabled="true"`,
  no `disabled` attribute, is the **same node** captured before the click, and `toHaveFocus()`.
- **R3** While `retrying`, clicking Retry does not call `onRetry` again (`toHaveBeenCalledOnce`).
- **R4** Click → `retrying` true → `retrying` false + `failed` false: `onRecovered` called once; live
  region "Loaded."; no `alert`.
- **R5** Click → `retrying` true → `retrying` false + `failed` true: the `alert` is **not** the node
  captured before (remount); the new Retry `toHaveFocus()`; live region empty.
- **R6** No click; `retrying` true → false + `failed` false: `onRecovered` not called; live region empty.
- **R7** No click; `retrying` true → false + `failed` true: `alert` is the same node; focus unchanged.

**Counterfactuals the test-writer runs and records (not committed):** R5 must go red if the `key` on
`FaultNotice` is removed; R2 must go red if `aria-disabled` is replaced by `disabled`; R6 must go red if
the per-fetch ref is replaced by a latched "was clicked" boolean.

Per site — one new file each, `<Page>RetryRecovery.test.tsx` beside the page, reusing that page's
existing `*FocusRefetch.test.tsx` / `*.test.tsx` harness (MSW handlers + store):

- **R8-R16** (S1-S9): first GET refused (500), Retry focused and clicked, second GET 200 →
  `getByRole('heading', { level: 1 })` `toHaveFocus()`. Red at HEAD: focus is `document.body`.
- **R17** (S8 Rules only): first GET 500, second GET held pending: after the click Retry is still in the
  document, the same node, focused. Red at HEAD (`isError` unmounts it).

### 5b. Characterisation (green before and after, unmodified)

- `RetryBanner.test.tsx`'s four existing tests (role/text, `onRetry` once, fault tokens, focus ring) —
  green at HEAD and **unmodified** after: FR-009 defaults make `<RetryBanner message onRetry />` behave as
  today.
- **C1** New, committed before the source change: `WallsPage.test.tsx` — first GET 500, second held
  pending; after clicking Retry the Retry node is still mounted and the same reference. Green at HEAD —
  pins #3073's premise that `error` survives the refetch for the `error !== undefined` sites.
- Every existing suite for S1-S10 green, unmodified: notably `OverlayEditPage.test.tsx:264` (S10 untouched),
  `CameraDetailPage.test.tsx` and `HeldEventTypesPage.test.tsx` `status` queries (D1), the five
  `*FocusRefetch.test.tsx` files.

## 6. Commit sequence (each typechecks alone; ADR-0087)

1. `test(3073): a retry on a failed list keeps its node while the read is in flight` — C1 (green).
2. `test(3073): RetryBanner announces, guards and hands focus on recovery (red)` — R1-R7.
3. `fix(3073): RetryBanner keeps an always-mounted live region and hands focus back on recovery`.
4. `test(3073): a successful retry focuses the page heading on nine pages (red)` — R8-R17.
5. `fix(3073): the nine RetryBanner pages wire failure, in-flight and recovery` (S1-S9).

Commits 2 and 4 are red-at-their-own-commit by design (CLAUDE.md "Builds is not passes").

## 7. Risks

- **jsdom focus fidelity.** jsdom does not blur a removed element the way a browser does, but
  `document.activeElement` becomes `body` on removal, which is what R8-R16 assert against. Phase 5
  observes it in a real browser (spec §7).
- **Effect ordering with RTK Query.** `pending` and `fulfilled` land in separate renders, so both edges
  are observed. If a test harness resolves synchronously, the test-writer holds the second response on a
  deferred promise (R17 pattern).
- **Unscoped `getByRole('alert')` in site tests** still finds one element: the live region has no role.

## 8. Follow-ups (text for the orchestrator, not `agent:ready`)

**OverlayEditPage's page-level Retry destroys itself and loses focus (S10, split from #3073).**
`OverlayEditPage.tsx:135-144` renders `RetryBanner` inside a branch on `chainFailed` (`isError`), which
goes false the instant Retry is clicked, swapping the whole tree for the read-only placeholder form; on
success the seeded form replaces it. Fixing it means deciding whether the banner coexists with the
placeholder form during the re-read, inside the seeding invariants of spec 156 / #2350. `RetryBanner`
already supports it after spec 429 (`failed`/`retrying`/`onRecovered`).
