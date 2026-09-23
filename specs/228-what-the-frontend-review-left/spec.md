# Spec 228 — What the frontend review left

**Issue**: #2306 · **Branch**: `fix/2306-frontend-review-nits` · **Phase**: 1 (Specify)
**Date**: 2026-09-23 · **Base**: `b8c14eb9` (`origin/develop`, fetched 2026-09-23)
**Context**: frontend only — `apps/shared`, `apps/management-web`, `apps/kiosk-web`. No bounded context, no C#.
**Engineer**: `frontend-engineer` · **Reviewer**: `frontend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **two colours, declared per item** (§6). RED for items 2, 3, 4;
CHARACTERISATION (observed green) for items 5, 6. Precedent: spec 146 declared the same
two-colour split for one PR.
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane; 4a's two colours), ADR-0139 (new
behaviour observed red), ADR-0036 (smallest change), ADR-0077 (Radix + native primitives;
item 2), ADR-0078 as amended by **ADR-0148** (tokens; the reason item 1 is deferred),
ADR-0109 (disjoint files), ADR-0129 (labels aged to the picture; item 5's subject)
**Constitution**: §IV (items 4 and 6 sit on the observability and presentation-buffer
code; **no leg's budget or timing changes**, §8), §Testing (both obligations), §II
(does not bind — no domain model)
**New ADR needed**: **No** (§9).

---

## 1. The premise, re-checked against `b8c14eb9`

Issue #2306 was filed 2026-09-13. Ten days later, **two of the six items have moved and
one is a duplicate of another open issue.** Every line reference was re-read.

| # | Issue claim | Status at `b8c14eb9` |
|---|---|---|
| 1 | Five hard-coded colours at `CameraViewer.tsx:361-362`, `OverlayEditor.tsx:69,86-87` | **Moved, and duplicated.** Spec 146 (#2339) extracted the label surface into `apps/shared/src/ui/composites/overlayLabelStyle.ts:38-39` (`rgba(255, 255, 255, 0.85)`, `#111827`); the editor's `0.92` copy is **gone**. The checkerboard is now `OverlayEditor.tsx:191`. **#2342** (open, `tech-debt`) owns converting exactly these literals — and the whole editor — to ADR-0148 semantic tokens, and is explicitly gated on **#2332** (the token system). Spec 146 §"Tokens" recorded the same deferral. |
| 2 | `GridDesigner.tsx:200-219` — six `role="radio"` buttons, all tabbable, no arrow keys; "Radix `RadioGroup` is already a dependency" | **Defect holds; two details wrong.** Lines 200-219 exactly. **Four** presets, not six (`GRID_PRESETS` is capped by `MAX_TILES = 4`). **`@radix-ui/react-radio-group` is not a dependency** of any workspace (`apps/shared/package.json:59-66` lists eight Radix packages; RadioGroup is not one). |
| 3 | `CameraViewer.tsx:340-345` — "Reconnecting…"/"Stream is offline" in a plain `<div>`, no `role="status"`; `<video>` at `:310` unnamed; the kiosk's badge at `CellPage.tsx:439` does it right | **Defect holds at new lines.** Overlay at `CameraViewer.tsx:417-422` (labels `:469-477`), `<video>` at `:380`. The cited "good" example is at `CellPage.tsx:554-561` **and is itself conditionally mounted** — the shape #2346 found is *not reliably announced*. It is not the pattern to copy (§5, FR-006). |
| 4 | `kioskLatency.ts:87` — unconditional `console.info` per sample | **Holds exactly** (`:87`, comment at `:81-86` says "it costs nothing"). **Consumers the issue does not mention**: spec 108's e2e harvest (`e2e/kiosk-shows-a-label-over-video.spec.ts:1203-1211`) and spec 225's render-leg CI gate read this line off the kiosk console. Both run against `vite dev` (`AppHost.cs:699,721` — `AddNpmApp(..., "dev")`), where `import.meta.env.DEV` is `true`, so a DEV gate leaves them intact (§7, A2). |
| 5 | `useWallAlignment.test.ts:314-315` — `toBeNull()` then `not.toBe(0)` | **Moved, and there is a second.** Now `useWallAlignment.test.ts:379-380`. The same tautology is at **`apps/shared/src/observability/labelDelay.test.ts:31-32`** (`labelDelayFor(0)`), which the issue did not name. Found by grepping the shape, not the file. |
| 6 | `wallAlignment.ts:425` — `wallTargetFrom([...stillHeld, ...markedWithLags])` rebuilt per marked tile | **Holds exactly** (`:425`, inside the `for (const camera of wasMarked)` loop at `:421`). Neither operand depends on `camera`. |

Not acted on, per the issue: the "hottest files" note, and `package.json:8`'s engine range
(explicitly left for a human).

---

## 2. Scope decision

**In scope: items 2, 3, 4, 5, 6. Deferred: item 1, to #2342.**

Item 1 is deferred rather than done because doing it now is wrong three ways:

1. **It is #2342's work.** #2342 converts the same literals as part of converting the whole
   editor, gated on #2332. Tokenising two literals against the seven-property flat file
   today produces names ADR-0148's two-layer migration renames.
2. **It cannot be characterised honestly.** The values are inline `style` strings.
   `OverlayLabelCharacterisation.test.tsx:73`, `OverlayLabelParity.test.tsx:80` and
   `OverlayEditorCharacterisation.test.tsx` pin the literal string. Replacing it with
   `var(--…)` changes what `element.style.background` reads back in jsdom, so those
   assertions would have to be **edited** — and under the lane an assertion that must be
   edited is a block, not an adjustment.
3. **The issue's own hedge is answered by #2342.** "If the contrast is deliberate … it wants
   a token and a one-line reason": the label deliberately does not follow the theme — it
   sits on video, not on the page. #2342 is where that token and reason belong.

**Action for phase 7** (orchestrator, not the engineer): comment on #2342 that #2306 item 1
is folded into it, with the moved line references from §1. No new issue.

**One PR, not two.** The five items own disjoint files (§11), each is small, and a
two-colour phase 4a is established (spec 146). Splitting by colour would produce two
PRs of trivial size for no review benefit. Phase 4a is **sequenced**, not blended (§6).

---

## 3. User stories

### US1 — The grid-size picker behaves as a radio group (P1, item 2)

A keyboard operator building a layout in the management console reaches the grid-size
picker with one Tab, changes the size with the arrow keys, and leaves with one Tab.

**Acceptance**

```gherkin
Scenario: One tab stop for the group
  Given the layout editor is open with grid 1×1 selected
  When the operator tabs into the "Grid size" group
  Then focus is on the "1×1" radio
  When the operator presses Tab again
  Then focus leaves the group, landing on the first tile's Camera select

Scenario: Arrow keys move the selection
  Given focus is on the checked "1×1" radio
  When the operator presses ArrowRight
  Then "1×2" is checked and focused
  And the tile grid shows two tiles

Scenario: Click still works (regression)
  When the operator clicks the "2×2" radio
  Then "2×2" is checked and the tile grid shows four tiles

Scenario: The group is still addressable by role and name
  Then there is a radio named "1×1", "1×2", "2×1" and "2×2"
  And the group is labelled "Grid size"
```

No server contact, so no conflict / bad-request / auth scenarios apply: the picker writes
form state only; submit validation is unchanged (spec 010). Auth is the existing page
guard.

### US2 — A screen-reader user hears the stream fail and recover (P1, item 3)

An operator using a screen reader on the camera detail page is told when the stream
starts reconnecting or goes offline, and the video element has a name.

**Acceptance**

```gherkin
Scenario: A reconnect is announced
  Given a CameraViewer that is live
  When its session goes to reconnecting
  Then the viewer's status region reads "Reconnecting…"

Scenario: The region pre-exists its message
  Given a CameraViewer that is live
  Then a status region exists and is empty
  # #2346: a region inserted with its text is not reliably announced

Scenario: Offline and error are announced with the painted wording
  When the stream is reported offline
  Then the status region reads "Stream is offline"
  When the read for the camera fails
  Then the status region reads "Viewer error"

Scenario: Recovery clears the region
  Given the status region reads "Reconnecting…"
  When the session returns to live
  Then the status region is empty

Scenario: The video is named
  Given a CameraViewer given the camera name "Line-1 Inlet"
  Then the video element's accessible name is "Live video: Line-1 Inlet"
  Given a CameraViewer given no camera name
  Then the video element's accessible name is "Live camera video"

Scenario: The painted overlay is not read twice
  When the viewer shows "Reconnecting…"
  Then the visible overlay is hidden from the accessibility tree
  And "Reconnecting…" is still visible on screen
```

Bad-request / conflict / auth: N/A (no request is introduced).

### US3 — A production kiosk does not log every latency sample (P2, item 4)

**Acceptance**

```gherkin
Scenario: Production build is silent
  Given the app runs as a production build (import.meta.env.DEV is false)
  When a latency sample is reported
  Then no "[latency]" console line is written
  And the sample is still POSTed exactly as before

Scenario: Development and test keep the line
  Given import.meta.env.DEV is true
  When a latency sample is reported
  Then console.info is called with "[latency]" and { measurement, camera, elapsedMilliseconds }
```

### US4 — Two tests stop asserting what the line above already proved (P3, item 5)

```gherkin
Scenario: A tile that reported and aged out has no frame age
  Given a two-tile wall where tile "departing" reported a lag of 150 ms
  And only the other tiles keep reporting
  When more than 15 s of settle cycles pass
  Then frameAgeFor("departing") is null
  # not 150: the stale figure must not survive. Proven non-vacuous by counterfactual.
```

The redundant `.not.toBe(0)` lines at `useWallAlignment.test.ts:380` and
`labelDelay.test.ts:32` are removed; the `toBeNull()` line above each stays.

### US5 — The settle cycle stops rebuilding a loop-invariant target (P3, item 6)

```gherkin
Scenario: Behaviour identical
  Given the existing settleAlignment suite (wallAlignment.test.ts)
  When the trial target is computed once before the marked-tile loop
  Then every test in the suite passes unmodified
```

---

## 4. Independent end-to-end test procedure

1. `npm test` in `apps/shared`, `apps/management-web`, `apps/kiosk-web` — all green.
2. Boot the stack (`dotnet run --project src/AppHost`), open management-web, open
   **Layouts → New**. Tab to "Grid size": one stop; ArrowRight/ArrowLeft change the size
   and the tile grid follows; Tab leaves to the first Camera select.
3. Open a live camera's detail page with a screen reader (NVDA or Narrator) running. Stop
   the stream (patch the MediaMTX path, per the repo's outage recipe). Hear
   "Reconnecting…" / "Stream is offline". Inspect the `<video>`: accessible name
   "Live video: <camera name>".
4. Open the kiosk (`vite dev`) with devtools: `[latency]` lines still appear.
   `npm run build && npm run preview` in `apps/kiosk-web`: no `[latency]` lines; the
   network tab still shows the `POST` per sample.
5. Run `e2e/camera-detail.spec.ts`, `e2e/layouts.spec.ts` and
   `e2e/kiosk-shows-a-label-over-video.spec.ts` — the last confirms spec 108/225's
   harvest still sees `[latency]` lines.

---

## 5. Functional requirements

- **FR-001** *(item 2)* The grid-size picker is a group of native
  `<input type="radio">` elements sharing one `useId()`-derived `name`, inside the existing
  `<fieldset>`/`<legend>Grid size</legend>`. The `div role="radiogroup"` and the
  `button role="radio"` elements are removed. Mirrors `BackdropControls.tsx:70-85`
  (spec 147) — the repo's existing radio group. **No new dependency**; the issue's
  Radix `RadioGroup` is not installed, and native radios give roving tab stop and arrow
  keys from the platform.
- **FR-002** *(item 2)* Each radio's accessible name is its preset label (`1×1` …), so
  `getByRole('radio', { name: '2×2' })` in `LayoutEditorDialog.test.tsx:184,207`,
  `e2e/layouts.spec.ts:74` and `e2e/support/seed-live-video-wall.setup.ts:124` resolves
  unchanged.
- **FR-003** *(item 2)* The visual treatment (active pill vs muted pill) is preserved, and
  the focused radio has a visible focus indicator.
- **FR-004** *(item 3)* `CameraViewer` renders one **always-mounted**, visually hidden
  (`sr-only`) `role="status"` element, addressable by `data-testid="camera-viewer-status"`.
- **FR-005** *(item 3)* Its text is exactly the label the overlay paints
  (`failedRead ? 'Viewer error' : labelFor(status, stream)`) while `status !== 'live'`,
  and empty while `status === 'live'`. The hint line is not announced.
- **FR-006** *(item 3)* The visible `ViewerOverlay` carries `aria-hidden="true"`, so the
  message is read once, from the region. It is not itself given `role="status"` — a
  region mounted with its content is the #2346 defect.
- **FR-007** *(item 3)* `CameraViewerProps` gains optional `cameraName?: string`. The
  `<video>` gets `aria-label` = `` `Live video: ${cameraName}` `` when given, else
  `"Live camera video"`. `CameraDetailPage.tsx:147` passes `record.name`. The kiosk's
  `CellPage` passes nothing (it holds no camera name; not widened here).
- **FR-008** *(item 4)* `reportKioskLatency` writes the `[latency]` console line only when
  `import.meta.env.DEV` is true. The `send(...)` call is unconditional, as today. The
  comment at `:81-86` is rewritten to state why (retained console buffer on a never-restarted
  wall) and that spec 108/225's e2e harvest depends on the stack running `vite dev`.
- **FR-009** *(item 5)* `useWallAlignment.test.ts` gains a test that a tile which reported
  and then aged out reports `frameAgeFor(...) === null`; the entailed `.not.toBe(0)` at
  `:380` and at `labelDelay.test.ts:32` is deleted.
- **FR-010** *(item 6)* In `settleAlignment`, the trial `wallTargetFrom([...stillHeld,
  ...markedWithLags])` is computed once, before the `for (const camera of wasMarked)`
  loop. No other line changes.

---

## 6. Phase 4a — colour per item, and the sequence

| Item | Colour | Why |
|---|---|---|
| 2 | **RED** | Adds keyboard behaviour that does not exist (one tab stop, arrows). |
| 3 | **RED** | Adds announcements and an accessible name that do not exist. |
| 4 | **RED** | Removes a console line **in production builds** — observable behaviour moves. Testable with `vi.stubEnv('DEV', false)`. |
| 5 | **CHARACTERISATION** | Test-only; production code already ages the sample out (`useWallAlignment.ts:144-146`). The new test arrives green by construction. Its evidence is a **counterfactual**: delete the `lagsRef.current.delete(tileKey)` line, observe the new test red, restore. Deleting an entailed assertion removes no failure mode (it had none), so it is not gate-weakening. |
| 6 | **CHARACTERISATION** | Pure refactor of a pure function. Evidence: `wallAlignment.test.ts` observed green before, **unmodified** and green after; plus a counterfactual proving the suite covers line 425 (e.g. force `trial = null`, observe "Takes a marked tile back…" / "Lets marked tiles recover against each other…" go red, restore). |

**Sequence** (test-writer, then engineer — never interleaved):

1. **4a-green** — run `wallAlignment.test.ts`, capture green verbatim; run the item-6
   counterfactual, capture red, restore. Write item 5's test, capture green; run its
   counterfactual, capture red, restore.
2. **4a-red** — write items 2, 3, 4's tests; capture each red verbatim. A test arriving
   green is a phase-4 failure.
3. **4b** — the engineer implements 2, 3, 4, 6 and the item-5 deletions against that
   evidence. Every characterisation file passes unmodified.

All four blocks of verbatim output go in the PR body.

---

## 7. Assumptions (marked, not buried)

- **A1** *(item 3)* Recovery to live **clears** the region and announces nothing. The
  region reports what is painted, and nothing is painted when live. Announcing "Live"
  would also fire on every initial connect of every tile. Revisit if an operator asks.
- **A2** *(item 4)* Spec 108/225's e2e harvest depends on the kiosk being served by
  `vite dev`. If e2e ever moves to a production build, the harvest goes silent — and spec
  225 FR-003 reports "no samples", never a pass, so it fails loudly rather than quietly.
- **A3** *(item 3)* No test in the repo queries `getByRole('status')` where a mounted
  `CameraViewer` would add a second match. Checked: `CameraDetailPage.test.tsx` mocks
  `CameraViewer`; `e2e/camera-detail.spec.ts:110,131,315` and
  `e2e/support/retire-e2e-cameras.teardown.ts:198` query it only after retirement, which
  unmounts the viewer (`CameraDetailPage.tsx:145`). Phase 5 re-runs those specs to confirm
  under Playwright's strict mode.
- **A4** *(item 2)* user-event 14.6.6 implements tab-destination for radio groups and
  arrow-key radio walking in jsdom. If it does not, the keyboard assertions move to a
  Playwright check in `e2e/layouts.spec.ts` rather than being dropped.

## 8. Latency budget impact (§IV)

**No leg's budget or timing changes.** Item 6 runs in the 2 s settle interval
(`useWallAlignment.ts:35`), not per frame; it removes work but no leg is claimed improved.
Item 4 removes a `console.info` in production builds only; the `POST` that §VII's
dashboard reads is untouched. Items 2 and 3 are off the event→overlay path (item 3 adds
one text node update per stream-state change, not per frame).

## 9. No new ADR

Every choice is covered: native radios under ADR-0077 (Radix *or* native — the backdrop
group is already native), live-region shape from #2346's recorded lesson, `import.meta.env.DEV`
gating already used by `DevCrashTrigger.tsx:8`, tokens deferred to ADR-0148/#2342.

## 10. Out of scope, noticed

- `CellPage.tsx:554-561` ("Overlay unavailable") is a `role="status"` mounted with its
  content — the #2346 shape. Not fixed here (the issue cites it as the good example; it is
  a different surface). Reported to the orchestrator for a follow-up decision.
- `OverlayEditor.tsx:184-201` and `BackdropControls.tsx:37-38` carry further hex literals;
  #2342's scope.

## 11. Files (disjoint per item — ADR-0109)

| Item | Production | Tests |
|---|---|---|
| 2 | `apps/management-web/src/features/layouts/GridDesigner.tsx` | new `GridDesignerKeyboard.test.tsx` (same folder) |
| 3 | `apps/shared/src/ui/composites/CameraViewer.tsx`; `apps/management-web/src/features/cameras/CameraDetailPage.tsx` | new `apps/shared/src/ui/composites/CameraViewerAnnouncement.test.tsx`; one test added to `CameraDetailPage.test.tsx` |
| 4 | `apps/shared/src/observability/kioskLatency.ts` | `kioskLatency.test.ts` (one test added) |
| 5 | — | `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts`; `apps/shared/src/observability/labelDelay.test.ts` |
| 6 | `apps/shared/src/observability/wallAlignment.ts` | `wallAlignment.test.ts` — **unmodified** |
