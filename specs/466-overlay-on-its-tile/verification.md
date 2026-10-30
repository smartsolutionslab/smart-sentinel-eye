# Verification note — spec 466 (phase 5, 2026-10-30)

Stack: local Aspire AppHost (Release build, `ASPNETCORE_ENVIRONMENT=Development`), seeded nine-tile wall, Playwright driving Chromium at 1920×1080, `--workers=1`.

## What was observed

The tile view opens from the layout designer on a real tile of the seeded wall, shows the overlay as the wall does, edits it in place, and shows the layout's tiles around it.

## How, and what the output was

- **Write right (R8, was unverified).** Signed in as the seeded `operator`; the session's `scope` read from the stored OIDC user: `openid … sse.overlays.read … sse.overlays.write … sse.layouts.read sse.layouts.write …`. `sse.overlays.write` is present, so `useCanEditOverlays` is true for the default operator.
- **SC-002, matches the wall.** `e2e/kiosk-overlay-tile-view-matches-the-wall.spec.ts` passed twice against the live wall (20.9 s, 36.1 s). Counterfactual: with `@container` removed from `TileFrame` it failed with `type size as a share of the viewer's width: wall 0.01667, view 0.05042`, and passed again once restored.
- **Neighbour captures (N−1).** Opened tile 1,1 of the nine-tile wall and recorded the WHEP requests the browser made: **9 POST and 9 DELETE** (one for the open tile, eight neighbours), the most sessions open at any moment **1**, all nine ended with a picture and none with a neutral backdrop, the whole queue in about 8 s (first POST at 8.9 s, last DELETE ≈ 17 s after sign-in). `[{"POST" … "DELETE" …}]` pairs strictly alternate.
- **SC-004, edit to frame.** Typed into the editor's text field and watched the display with a MutationObserver from the `input` event to the changed text: **195 ms and 219 ms** (two edits, one run). Limit is 1 s.
- **A defect this found, fixed.** The first live attempt showed the edit being discarded (field stayed `{{spec056value…}}`, frame stayed `BEFORE`): `TileView` built a new `editTarget` object every render, and `OverlayDraftForm` re-seeds itself whenever that changes. The unit tests could not see it because they stand in for the form. Red test `the form keeps its edit target across an edit` (`expected 2 to be 1`), then fixed with `useMemo` (`803e3961`, `678a46ee`). The second live run produced the figures above.

## Latency

Not on the event-to-overlay path: the tile view is operator-initiated and reads the same data as before. Kiosk-reachable code changed only by an `export` keyword on two functions in `CameraViewer.tsx`, an optional `size` prop on `Dialog`, and an optional `initialCamera` prop on `OverlayEditor`.

SC-006 was nevertheless measured as asked, with spec 108's e2e (`kiosk-shows-a-label-over-video.spec.ts`), twice without and twice with the three changed shared files (the "without" runs restore them from `origin/develop`). Composite + render leg, p50 of one attempt each: **before 64.1 ms and 55.9 ms, after 61.9 ms and 67.5 ms.** The spread within each side is as large as the difference between them, so no change is measured; it is not a demonstration of equality. The same run reports the leg as above its 50 ms budget on this machine both before and after (the report calls it report-only, FR-019 not met). In **all four** runs the span test (`the span from a value being submitted to it being visible`) failed, including the two on develop's code, so these are partial figures from a test that does not complete on this machine; the cause was not investigated.

## What was not covered

- The pixels. SC-002 compares computed style, boxes and wrap, not rasterised pixels of video, which differ by design (live video on the wall, a still in the view).
- SC-005 (not buildable, unresolved analysis item).
- A real stale-version conflict against the server (the unit test stands in for the 409), and publishing the edited draft from the view (the view saves a draft only).
- The visual layout of the dialog (large frame, the editor under it, the context grid under that) was not judged by eye; no screenshot was reviewed.
- The cleanup teardown `retire-e2e-cameras` crashed twice (`page.goto: Page crashed`), leaving e2e cameras on the dev database. Unrelated to the spec; not investigated.
- The Windows run of `scripts/wait-for-e2e-stack.sh` never completes its gateway probe (it cannot resolve the gateway port from the `@fs` path); the stack was verified by its status file instead.
