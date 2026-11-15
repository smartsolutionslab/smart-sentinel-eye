# Feature Specification: A republished overlay must not leave its tile blank

**Feature Branch**: `fix/3275-reordered-overlay-flake`

**Created**: 2026-11-15

**Status**: Ready for plan. No `[NEEDS CLARIFICATION]` markers remain.

**Issue**: #3275 (on Project #13). **Lane**: autonomous (ADR-0144), `agent:ready`.

**Input**: "e2e: kiosk-paints-a-reordered-overlay flakes in CI, once to one attempt from red (tile shows zero overlay nodes after the
republish)" (#3275).

**Governing decisions**: ADR-0088 (one RabbitMQ queue per consuming context *per event type*, so two event types carry no ordering
relative to each other), ADR-0124 (order guarantees are per queue, never assumed across them), ADR-0152 (SignalR is the push transport;
it preserves send order, not the order of independent server-side relays), ADR-0129 (an overlay is not frame-synced; nothing here
changes that), ADR-0139 (red first). No new ADR is needed: the fix makes the kiosk tolerate a reordering the existing decisions already
permit.

**Latency budget (§IV): leg touched: Event → overlay state (≤ 200 ms).** Today, on the failing interleaving, that leg never completes:
the republished overlay is fetched but never painted, until the next publish or a page reload. The fix adds one map lookup per pushed
archive frame; no measurable cost on the leg.

## The issue's premises, checked (2026-11-15, `origin/develop` at `49950314`)

| # | Claim in #3275 | Verdict | Evidence |
|---|---|---|---|
| 1 | The spec flakes in the `kiosk` project and passes only via `retries: 2` | **True** | All five runs the issue names have a failed attempt of this test in `playwright-report-4-of-4`: 43514387662, 43690702471, 43694517856 (two), 43698057177, 43705184965. Six failed attempts in total. |
| 2 | After the republish the tile holds zero overlay nodes for the whole 90 s | **True** | `error-context.md` of all six attempts: `Received: 0` / `Array []`. |
| 3 | Cause undiagnosed; three candidates (push never arrives / tile drops it / the fixture) | **Superseded: diagnosed, it is the second** | See "Root cause" below. |
| 4 | The failure-time trace was never read | **Was true; now read** | `trace.zip` of 43694517856 retry #1 (artifact still present, 2026-11-15). |
| 5 | Possibly on the event-to-overlay path | **True** | The tile's overlay state is wrong after a pushed frame; §IV leg *Event → overlay state*. |
| 6 | Spec 497's one-origin move may be involved | **False** | 43514387662 predates the move and fails with the identical signature; the mechanism below involves no origin. |
| 7 | The spec's own fixture (overlap, branch + PATCH + republish) may be at fault | **False** | The kiosk's refetch after the republish returned the reordered revision correctly (below). Overlap and reorder are irrelevant; any republish of a bound overlay exposes it. |

## Root cause (evidence, not inference where it says so)

1. **Every one of the six failed attempts shows the tile's *Overlay unavailable* badge** in its page snapshot
   (`status [ref=f2e9]: Overlay unavailable`). A passing attempt does not.
2. **The kiosk received the republished overlay correctly.** In the retry-1 trace of run 43694517856, the operator publishes revision 2
   at 08:55:27.671Z; the kiosk re-fetches `GET /overlay-designer/overlays/{id}` at 08:55:27.693Z (`ETag "4"`), and the body is
   `revision 1 Archived [Box, Text, Ellipse]`, `revision 2 Published [Ellipse, Text, Box]`. That refetch is triggered only by the
   `OverlayRevisionPublished` hub frame (`useOverlayHubHandlers.ts`, `onOverlayPublished` is the only `invalidateTags(Overlay, id)` outside
   a hub reconnect, and the console shows no reconnect after 27.524Z). So the Published frame arrived and the data is right.
3. **With a Published revision fetched, the badge has exactly one remaining source**: `Tile`'s `unavailable` prop, which is
   `unavailableOverlays.has(overlay)` (`LayoutGrid.tsx:168`), which only `onOverlayArchived` ever adds to (`useOverlayHubHandlers.ts:133`).
   `useTileOverlay`'s derived branch (`overlay !== undefined && publishedOverlay === undefined`) is false for that body.
   `useHeldOverlay` then renders `undefined` for an unavailable tile, which is the zero nodes.
4. **Why an archive frame lands after the publish frame.** `Overlay.Publish` raises `OverlayRevisionArchivedDomainEvent(prior = 1)` and
   then `OverlayRevisionPublishedDomainEvent(2)` in one transaction (`Overlay.cs:161-177`). They leave as two integration events on two
   LayoutComposition queues (`layout-composition.<FullName>`, `WolverineDefaults.cs:123-124`, ADR-0088), each drained by its own listener,
   and each relay awaits `FabsReferencingOverlayQueryHandler` before broadcasting. Nothing orders the two broadcasts.
   *Inference (the trace does not record WebSocket frames)*: on the failing runs `OverlayRevisionPublished(2)` reached the kiosk first —
   clearing nothing and triggering the refetch — and `OverlayRevisionArchived(1)` second, flagging the overlay with nothing left to
   clear it. Points 1-3 leave no other path to the observed DOM.
5. **The kiosk ignores the revision number on the archive frame.** `onOverlayArchived` flags the whole overlay whatever revision the frame
   names, so an archive of the *superseded* revision blanks the tile showing its *successor*.

**Product consequence**: on a 24/7 wall, any operator republish of a bound overlay can blank that overlay on every tile showing it, with a
warning badge, until the next publish or a reload. The e2e spec found a real defect; the spec is correct and is not edited.

## User Scenarios & Testing

### User Story 1 — A republished overlay stays painted whatever order the two frames arrive in (Priority: P1)

An operator republishes an overlay bound on a live wall. The wall shows the new revision, never the *Overlay unavailable* badge, whether
the superseded revision's archive frame arrives before or after the new revision's publish frame.

**Why this priority**: it is the whole defect; one story, one slice.

**Independent test**: a kiosk-web component test fires `onOverlayPublished(revision 2)` then `onOverlayArchived(revision 1)` through the
captured hub callbacks and asserts the tile is not flagged; end to end, `e2e/kiosk-paints-a-reordered-overlay.spec.ts`, unmodified,
passes on its first attempt in CI.

**Acceptance scenarios**

```gherkin
Scenario: the superseded revision's archive arrives after the new revision's publish (the observed order)
  Given a wall tile bound to overlay O, showing its Published revision 1
  When the kiosk receives OverlayRevisionPublished for O revision 2
  And then receives OverlayRevisionArchived for O revision 1
  Then the tile is not flagged "Overlay unavailable"
  And the tile paints revision 2's elements

Scenario: the archive arrives first (the order that already works)
  Given a wall tile bound to overlay O, showing its Published revision 1
  When the kiosk receives OverlayRevisionArchived for O revision 1
  And then receives OverlayRevisionPublished for O revision 2
  Then the tile is not flagged "Overlay unavailable" once the publish frame is handled

Scenario: a genuine archive of the revision being shown still flags the tile (conflict: must not regress)
  Given a wall tile bound to overlay O, and the kiosk has received OverlayRevisionPublished for O revision 2
  When the kiosk receives OverlayRevisionArchived for O revision 2
  Then the tile is flagged "Overlay unavailable"

Scenario: an archive with no publish seen this session still flags the tile (must not regress)
  Given a wall tile bound to overlay O, and no OverlayRevisionPublished for O this session
  When the kiosk receives OverlayRevisionArchived for O revision 2
  Then the tile is flagged "Overlay unavailable"

Scenario: an archive for an unbound overlay changes nothing (bad request analogue)
  Given no tile bound to overlay P
  When the kiosk receives OverlayRevisionArchived for P revision 1
  Then no tile is flagged
```

*Auth*: N/A. The hub's authorization and fab routing are untouched; the frames in question are already authorized deliveries.

### Edge cases

- **Revert then republish of the same number.** `Revert(2)` raises `Archived(2)`; a later `Publish(2)` raises `Published(2)`; a later
  `Revert(2)` raises `Archived(2)` again. The comparison is strict (*older than*), so an archive naming the newest known published revision
  always flags.
- **A kiosk that loads after the republish and only then receives a late `Archived(1)`.** It has seen no publish push, so the frame still
  flags. The window is the relay's own skew (milliseconds) and needs the kiosk to connect inside it; not observed, **out of scope**, and
  recorded so it is not mistaken for covered.
- **Hub reconnect.** `onReconnected` refetches overlays but does not clear the pushed-archive flags; unchanged by this spec, out of scope.

## Requirements

- **FR-001** The kiosk MUST remember, per bound overlay, the highest revision number it has received in an `OverlayRevisionPublished`
  frame this session.
- **FR-002** An `OverlayRevisionArchived` frame whose `revisionNumber` is **lower** than that remembered revision MUST NOT flag the overlay
  unavailable.
- **FR-003** Every other `OverlayRevisionArchived` frame for a bound overlay MUST flag it exactly as today; every
  `OverlayRevisionPublished` frame MUST clear the flag and invalidate the `Overlay` and `OverlaySnapshot` caches exactly as today.
- **FR-004** No backend, contract, hub message or e2e spec changes. `e2e/kiosk-paints-a-reordered-overlay.spec.ts` is not edited.
- **FR-005** The red test MUST be observed failing on the unfixed code, and its failure quoted in the PR (ADR-0139).

## Success criteria

- **SC-001** The new component test is red before the fix and green after, with the existing tests in
  `CellPageLiveUpdates.test.tsx` passing unmodified.
- **SC-002** In the CI full-stack e2e job on the PR, `kiosk-paints-a-reordered-overlay` is reported **passed, not flaky** (first attempt).
  Read from the Playwright report, not the job status. One run is evidence, not proof: the test failed its first attempt in 5 of the 6
  runs the issue tabulates, so a first-attempt pass is meaningful but the issue stays honest about it.

## Out of scope

- Ordering integration events across queues (a backend change against ADR-0088/0124; would need an ADR and is not needed).
- Clearing archive flags on hub reconnect; the late-joining-kiosk window above.
- Any change to `retries: 2`.
