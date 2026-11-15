# Implementation Plan: A republished overlay must not leave its tile blank

**Spec**: [spec.md](./spec.md) | **Tasks**: [tasks.md](./tasks.md) | **Issue**: #3275 | **Lane**: autonomous (ADR-0144)

## Summary

The kiosk flags a whole overlay unavailable on any pushed `OverlayRevisionArchived` frame, ignoring the revision it names. A republish
emits `Archived(prior)` and `Published(new)` as two integration events on two independently drained queues (ADR-0088), so the kiosk can
receive them in either order. When `Archived(prior)` comes second, the tile is blanked indefinitely. Fix: in the kiosk's hub handlers,
remember the revision each overlay was last pushed as Published, and ignore an archive frame naming any **other** revision.

## Constitution and ADR check

| Rule | Status |
|---|---|
| §IV latency budget | Leg *Event → overlay state*: restores completion on the failing interleaving; one `Map` lookup added. |
| ADR-0088 / ADR-0124 (no cross-queue order) | Respected: the consumer is made order-tolerant; the backend is not asked for an ordering it does not promise. |
| ADR-0152 (SignalR) / hub contract | Unchanged. `OverlayRevisionArchivedMessage.revisionNumber` and `OverlayRevisionPublishedMessage.revisionNumber` already exist (`apps/shared/src/realtime/layoutHub.ts`). |
| ADR-0075 (RTK Query) | Unchanged: the publish frame's tag invalidation stays as is. |
| Bounded contexts / `Shared.Contracts` | No .NET change, no contract change. |
| ADR-0139 / constitution §Testing | Behaviour-changing: red first. |
| New ADR needed? | **No.** |

## Where the change lives

**One production file**: `apps/kiosk-web/src/features/cell/useOverlayHubHandlers.ts` (spec 229 extracted the hub handlers here; the
existing `overlayTextVersionsRef` is the precedent for a per-overlay monotonic mark kept in a ref).

- Add `lastPublishedRevisionsRef = useRef<Map<string, number>>(new Map())` (per hook instance, never pruned).
- `onOverlayPublished`: **before** the `boundOverlays` guard (the hook runs above `LayoutGrid`'s loading return, so a publish landing
  during a layout fetch must still be marked), `set` the map entry to `message.revisionNumber` (last by arrival, **not** max);
  everything else in the handler unchanged (FR-003).
- `onOverlayArchived`: after the existing `boundOverlays` guard, return without flagging when a Published frame was seen for the overlay
  and `message.revisionNumber !== lastPublished` (FR-002). With none seen, flag as today.
- A short why-comment pointing at ADR-0088 (the two frames travel on different queues) stating the trade-off; nothing more.

**Why not max (first design, rejected in review).** Revision numbers are not ordered by publish time: `Overlay.BranchDraft` uses
`MaxRevisionNumber().Next()` and `Revert` keeps its number (`Overlay.cs:129`, `:184-192`; management-web `chainView.ts:32-35` and
`OverlaysPage.tsx:236-240` record the two-open-drafts path), so a lower draft may be published after a higher one and a max rule would hide
its later genuine archive. Every `Archived(n)` names a revision Published when raised, so it is genuine iff `n` is the current published
revision.

**Accepted trade-off**: an `Archived(n)` arriving before its own `Published(n)` is ignored; the late `Published(n)` clears any flag as
before, so it is not a regression. **Dependency**: one overlay's Published frames staying in order on their single queue; a kiosk that
missed one can ignore a real archive until the reconnect resync.

**Rejected alternatives**

- *Compare against the fetched overlay's Published revision in `Tile`/`useTileOverlay`.* Covers the late-joining-kiosk edge case too, but
  in the observed order the archive frame lands before the refetch does (≈ 20 ms in the trace), so the tile would flash the badge and
  drop its elements before recovering. Two mechanisms for one defect; not needed for the observed failure.
- *On an archive frame, just invalidate and refetch, and derive unavailability only from fetched state.* Removes the immediate hide spec
  004/011 relies on for a genuine archive, and changes more behaviour than the defect calls for.
- *Order the two relays in LayoutComposition* (single queue, or one combined event). Contradicts ADR-0088's per-type queues; needs an ADR.

## Tests

**Red (new behaviour)** — `apps/kiosk-web/src/features/cell/CellPageLiveUpdates.test.tsx`, in the existing
`describe('Pushed overlay archive and publish frames (spec 229 US1, #2321)')`, reusing `aTwoTileWall()`, `capturedCallbacks` and
`unavailableBadgeIn` (no new harness):

1. *Does not flag a tile when the superseded revision's archive frame arrives after the new revision's publish frame* —
   `onOverlayPublished(ov-1, revision 2)` then `onOverlayArchived(ov-1, revision 1)`; assert no badge on tile A. **Red today** (the badge
   appears).

**Guards for the retained behaviour** (spec scenarios 3 and 4; expected green before and after — they pin FR-003 against an
over-broad fix, e.g. ignoring every archive after any publish):

2. *Still flags a tile when the archive names the revision last pushed as published* — `onOverlayPublished(ov-1, 2)` then
   `onOverlayArchived(ov-1, 2)`; badge present.
3. Scenario 4 (archive with no publish seen) and scenario 5 (unbound overlay) are already covered by the existing
   *Flags only the tile bound to the overlay a pushed OverlayRevisionArchived names*; scenario 2 by the existing
   *Clears a pushed-archive flag … when OverlayRevisionPublished follows*. Those stay **unmodified**.

Test 2 is a guard, not the red evidence: it passes on today's code. The PR quotes test 1's red output.

**Added after review (red before the corrected rule)**:

- `CellPageLiveUpdates.test.tsx`: *Does not flag a tile when the archive frame names a revision other than the last one pushed as
  published* (red under the max rule).
- Sibling file `CellPageStaleArchiveFrames.test.tsx` (14 tests, adversarial cases): cross-overlay isolation, stale archive after a genuine
  archive and republish, genuine archive of a lower revision published after a higher one (red), archive of a higher revision after a lower
  was published over it (red), publish during layout loading, publish before any tile binds, duplicate publish, archive-before-publish,
  every tile binding the overlay, rebound tiles, unbind/rebind, and no mark carried into a fresh mount.

**Counterfactual**: reintroduce `Math.max` in the Published handler; the two red sibling-file tests above go red. Revert. Record in the PR.

**End to end**: `e2e/kiosk-paints-a-reordered-overlay.spec.ts`, **unmodified**, in CI's full-stack job. No local stack (owner's choice:
CI is the first proof for e2e on this machine).

## Verification (phase 5)

- Unit: `pnpm --filter kiosk-web test` (or the workspace equivalent), `tsc --noEmit`, `eslint --max-warnings 0`, `prettier --check` on the
  touched files; the frontend PR also runs the .NET architecture tests in CI (they scan `.tsx`).
- CI: download the PR run's `playwright-report-4-of-4` (or whichever shard holds the `kiosk` project) and read the test's outcome in the
  report: **passed on first attempt**, not flaky (SC-002). Quote it in the verification note with the run id. A flaky outcome means the
  diagnosis is incomplete: read that attempt's `error-context.md` for the *Overlay unavailable* badge before anything else.
- §IV: no figure is re-measured; the change removes a non-completing case and adds no work on the leg's hot path.

## Risks

- **The inference in spec "Root cause" point 4** (frame order) is not directly observed: WebSocket frames are not in the Playwright trace.
  Points 1-3 are observed and eliminate every other source of the badge, so the red test reproduces the only interleaving that yields the
  observed DOM. If SC-002 fails, this is the first thing to revisit.
- The fix is per kiosk session (a ref). A page reload resets it, which is fine: a reload refetches the overlay's true state.
