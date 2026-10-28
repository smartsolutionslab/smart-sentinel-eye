# Quickstart: verifying spec 466

Run in the supervised lane, phase 5. One machine, one Aspire stack: stop any other AppHost first.

1. Boot the stack the way `e2e` does (seeded nine-tile wall).
2. Sign in as an operator with overlay and layout rights. Open **Layouts**, open the seeded wall in the designer.
3. On a populated cell choose **Open tile view**.
   - Expect: the camera picture (or a neutral backdrop with a notice), the cell's overlay with resolved text, the other tiles around it as stills, the open tile marked.
4. Change the display shape to 9:16 and back. The frame and neighbours follow; nothing is saved.
5. Move an element and edit its text. Expect the frame to change as you drag, and the change to reach the frame within 1 s of the edit settling (SC-004).
6. Sign in as an operator **without** overlay-write. Expect a read-only view with a notice (FR-012).
7. Compare against the wall: `e2e/overlay-tile-view-matches-the-wall.spec.ts` (SC-002). Quote its output in the verification note.
8. Confirm the wall is unchanged: publish nothing, watch the kiosk; the event→overlay figure from spec 108's e2e is not worse (SC-006). Quote the before/after run, twice.

Not covered by this quickstart: the chat editor (out of scope), exact usage counts (lower bound by design).
