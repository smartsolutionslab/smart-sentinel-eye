# Plan — Spec 229, the hub handlers CellPage carries

**Spec:** `specs/229-the-hub-handlers-cellpage-carries/spec.md`
**Issue:** [#2321](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2321)
**Colour:** behaviour-preserving (characterisation, observed green) — ADR-0144

## Constitution / ADR check

| Rule | Status |
|---|---|
| ADR-0036 smallest change; refactor ≠ fix | Pure move. No expression inside a moved handler body changes. Comments that point at moved code are re-pointed; no other prose edits. |
| ADR-0144 characterisation | 53 existing tests + US1 additions, captured green before, passing unmodified after. Proof = empty `git diff` on the test file between the US1 commit and the tip. |
| ADR-0145 fab filter | Moved verbatim, including the version-mark ordering the `Does not let another fab's frame advance the version mark` test pins. |
| ADR-0074 two apps | Kiosk-only; nothing enters `apps/shared`. |
| ADR-0109 disjoint files | Two files are shared by both phases (see Tasks); no `[P]` fan-out is useful at this size. |
| §IV latency | Event → overlay state leg: same work, no added render. No impact. |
| Bounded contexts / Shared.Contracts | N/A — frontend only, no backend or contract change. |

No ADR needed: the shape is `useWallAlignment`'s (spec 045), already accepted.

## Where things live after the change

Directory: `apps/kiosk-web/src/features/cell/`.

### New — `useOverlayHubHandlers.ts`

Mirrors `useWallAlignment.ts`: a file-level doc comment saying what the hook
owns and why a *wall* owns it; an exported result interface with a doc comment
per member; positional parameters; plain module-private helpers at the bottom.

```ts
export interface OverlayHubHandlers {
  /** Spread into `useLayoutLifecycle`. Fresh each render — see below. */
  handlers: Required<Pick<UseLayoutLifecycleOptions,
    'onOverlayPublished' | 'onOverlayArchived' |
    'onResolvedOverlayTextChanged' | 'onOverlayHighlightChanged'>>;
  /** Overlays a pushed OverlayRevisionArchived has flagged (spec 011 path). */
  unavailableOverlays: ReadonlySet<string>;
  /** Overlays with an active highlight (spec 010 US3, ADR-0112 §5). */
  highlightedOverlays: ReadonlySet<string>;
  /** Stable. Tile → page placeholder verdict (spec 141 site 3). */
  onLabelVerdict: (overlayIdentifier: string, hasPlaceholder: boolean) => void;
}

export function useOverlayHubHandlers(
  tiles: readonly LayoutTile[],
  wallFab: string | undefined,
): OverlayHubHandlers
```

`UseLayoutLifecycleOptions` is imported as a type from
`../revocation/useLayoutLifecycle.js`, which `CellPage` already depends on.

**Moves in, verbatim** (from `CellPage.tsx` at `b8c14eb9`):

| What | Lines now |
|---|---|
| `useDispatch<AppDispatch>()` (now called inside the hook) | 40 |
| `unavailableOverlays`, `highlightedOverlays` state | 92-93 |
| `overlayTextVersionsRef`, `highlightExpiryRef`, `highlightTimersRef` | 96-103 |
| `textWithoutFabCountRef`, `highlightWithoutFabCountRef` | 108-109 |
| `staticLabelOverlaysRef`, `reportedStaticPushRef`, `onLabelVerdict` (`useCallback([])`) | 121-125 |
| highlight-timer cleanup `useEffect` | 127-135 |
| `boundOverlays = tilesToBoundOverlays(tiles)` and `startHighlight` (with its `react-hooks/purity` disable) | 174-200 |
| handler bodies `onOverlayPublished`, `onOverlayArchived`, `onResolvedOverlayTextChanged`, `onOverlayHighlightChanged` | 210-219, 220-305 |
| `countReportableSkew`, `tilesToBoundOverlays`, `withAdded`, `withRemoved` | 630-642, 671-680, 686-698 |

With their comments. Each comment moves with the code it explains.

### New — `wallBindings.ts`

`boundOverlayIn` and `namedFab` (lines 657-669), exported, with their doc
comments. **Why a second new file:** both are read by `CellPage` (`Tile`, the
layout-fault effect) *and* by the hook (`tilesToBoundOverlays`, the two fab
guards). Left in `CellPage` they would force the hook to import from
`CellPage`, which imports the hook — a cycle. This is the minimum that avoids it,
not a utilities module; nothing else goes in it.

### Stays in `CellPage.tsx`

- Route/auth/token wiring (`useParams`, `useNavigate`, `useAuth`,
  `accessTokenRef`, `getToken`), `useGetLayoutQuery`, `published`, `tiles`,
  `wallFab` and its ADR-0145 comment.
- `useWallAlignment(tiles.length, getToken)`.
- **`reportedLayoutFaultsRef` and the layout-fault `useEffect`** (spec D1) — driven
  by the layout query, not a hub frame; its `published === undefined` coupling
  stays next to `published`.
- `useLayoutLifecycle({...})`, now:
  `accessTokenFactory`, `enabled`, `onArchived`, `...overlayHub.handlers`, `onReconnected`.
  No key overlaps, so spread order cannot clobber anything.
- The navigate-away effect, render tree, `Tile`, `EmptyCell`, `GridCell`,
  `buildGridCells`, `positionKey`, `FullScreen`.
- Render reads `overlayHub.unavailableOverlays`, `overlayHub.highlightedOverlays`,
  `overlayHub.onLabelVerdict` where it read the locals.

Imports that leave `CellPage`: `overlaysApi`, `systemVariablesApi` (value imports;
the query hooks stay), `useDispatch`, `AppDispatch`, `useState`.
`logResilienceEvent` stays (layout-fault effect).

Expected size: `CellPage.tsx` ≈ 450 lines, `useOverlayHubHandlers.ts` ≈ 260,
`wallBindings.ts` ≈ 30.

## Invariants the move must keep (each already pinned by a test)

1. **Hook call position.** Call `useOverlayHubHandlers` exactly where the state
   block starts today — after `useWallAlignment`, before the layout-fault effect.
   Effects run, and clean up, in declaration order; this keeps the timer-cleanup
   effect's position relative to the others identical.
2. **Handlers are not memoised.** `useLayoutLifecycle` keeps the latest options
   in a ref it refreshes every render, so fresh closures are how the handlers see
   the current `boundOverlays` and `wallFab`. Wrapping them in `useCallback`
   would be a behaviour change.
3. **`onLabelVerdict` stays stable** (`useCallback(..., [])`). It is in `Tile`'s
   effect deps; the spec 204 `Re-renders no tile across five settle cycles` test
   depends on its identity.
4. **Fab guard before version mark** in `onResolvedOverlayTextChanged`, and the
   static-label report before the version guard — order untouched.
5. **`performance.now()`** for highlight expiry, never `Date.now()`.
6. **Same number of `useState`s** — two, no new state, no new effect → no
   extra render on the Event → overlay state leg.

## Risks

- **R1 — a coverage hole passes as characterisation.** Two of the four handlers
  had no test (spec §0). Mitigated by US1, and each US1 test is proven able to fail
  by counterfactual (delete the line it pins, observe red, restore).
- **R2 — `react-hooks` lint rules flag moved code differently in a hook.** The
  `purity` disable moves with `startHighlight`. If a *new* rule fires, that is a
  stop-and-report, not a new suppression (ADR-0144: no new suppression to reach green).
- **R3 — a comment left pointing at code that moved** (e.g. CellPage's `wallFab`
  comment "both hub handlers below", `boundOverlayIn`'s "All four readers …
  `tilesToBoundOverlays` below"). Re-point them; do not rewrite them.
