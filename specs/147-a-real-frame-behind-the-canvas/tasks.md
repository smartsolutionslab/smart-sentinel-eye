# Spec 147 — Tasks

**Engineer:** `frontend-engineer`. **Reviewer (phase 6):** `frontend-reviewer`.
No security review: no auth model change, no new endpoint, no new scope, no
secret. The token passed is the one `CameraDetailPage` already passes.

**Format:** `[ID] [P?] [Story]`. `[P]` = disjoint files, safe to run in parallel
(ADR-0109).

---

## Phase 4a — the two colours

Both colours are in play. **Ambiguity resolves to red** (CLAUDE.md).

### 4a-green: characterisation, captured BEFORE any implementation

`OverlayEditor` has no test file of its own today, so the covering tests are
written first — a refactor with no covering test is a rewrite.

- [ ] **T001 [P] [US1]** — Write `apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx`
      (`// @vitest-environment jsdom`) pinning today's behaviour:
      1. the canvas paints
         `repeating-linear-gradient(45deg, #1f2937, #1f2937 12px, #111827 12px, #111827 24px)`
         **byte-for-byte** (FR-002);
      2. the canvas is 800×450 (FR-019);
      3. the label renders at the authored normalized coordinates;
      4. `onChange` receives clamped `[0,1]` geometry on drag and on resize;
      5. the text input and the font slider emit their fields.
      **Run it and record it GREEN.** Quote the output in the PR.
      **These must pass unmodified after T009–T012.** An assertion that has to be
      edited is evidence the behaviour moved — block, do not adjust.

### 4a-red: new behaviour, observed FAILING

- [ ] **T002 [P] [US2]** — Write `apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx`:
      checkerboard is the default; `white` paints `#ffffff`; `black` paints
      `#000000`; `captured` is disabled with no frame; a captured frame renders
      `contain`/`center`/`no-repeat` over `#000000` (FR-001–FR-004, FR-019);
      changing backdrop emits **no** `onChange` (FR-005). **Observe RED.**

- [ ] **T003 [P] [US1]** — Write `apps/shared/src/ui/composites/FrameCapture.test.tsx`
      using the `FakePeerConnection` + duck-typed SDP `fetch` double copied from
      `CameraViewerMedia.test.tsx:64-115` (copied, not shared — that file is a
      characterisation and must not be touched), and `vi.useFakeTimers()`:
      - **FR-014, the negative:** with the editor open and no capture in flight,
        **zero** peer connections are constructed;
      - **FR-007/FR-008:** pressing Capture with a camera selected constructs
        **exactly one**;
      - **FR-009:** nothing is captured before the session reports `live`;
      - **FR-010:** on `live`, the session closes and a WHEP `DELETE` is issued
        against the session URL (assert on the `fetch` stub, not on a mocked
        method);
      - **FR-011:** no frame within 10 s → session closed, backdrop unchanged,
        alert shown;
      - **FR-012/FR-013:** cancel, camera change mid-flight, and unmount each
        close the session;
      - **FR-016:** a stream-lookup error shows the same alert and changes
        nothing else.
      **Observe RED.**

- [ ] **T004 [P] [US3]** — Write the dialog-level reds in
      `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx`:
      after a failed capture the form is still submittable; the submitted body is
      **byte-identical** to today's `{ name, label }` (no preview state leaks);
      `getToken`'s identity survives a token change (R5, mirroring
      `CameraDetailPage`'s test); closing the dialog mid-capture closes the
      session. **Observe RED.**

> `test-writer` runs T001–T004, returns the **verbatim** output, and that output
> is the engineer's brief. The engineer may not edit these tests to pass.

---

## Phase 4b — the implementation

**T005 is foundational and blocks T006–T008.** It defines the prop surface and
the state the rest hangs off.

- [ ] **T005 [US1]** — `OverlayEditor.tsx`: add the optional
      `getToken?: () => Promise<string | null>` prop, the
      `backdrop: 'checkerboard' | 'captured' | 'white' | 'black'` state
      (default `checkerboard`) and `capturedFrame: string | null`. Drive the
      canvas `background` from `backdrop` per plan.md's table, keeping the
      checkerboard string byte-for-byte. **Do not touch the `<Rnd>` style
      object** — that is #2354's territory and its evidence (invariant 3).

- [ ] **T006 [P] [US1]** — `apps/shared/src/ui/composites/useFrameCapture.ts`:
      the `CaptureState` machine, the active camera, the 10 s timer, `capture()`
      and `cancel()`. Owns no WHEP session.

- [ ] **T007 [P] [US1]** — `apps/shared/src/ui/composites/FrameGrabber.tsx`:
      mounted only while a capture is in flight. `useWhepSession` + a 1×1
      transparent `<video>` (**not `display:none`** — R3). On `status === 'live'`,
      `drawImage` + `toDataURL('image/png')` inside a `try/catch` with an
      explicit null check on `getContext('2d')` (R1, R2), then `onCaptured`. On
      `error`/`offline`, `onFailed`. Teardown is the unmount — add no manual
      release path.

- [ ] **T008 [P] [US2]** — `apps/shared/src/ui/composites/BackdropControls.tsx`:
      the four-way backdrop selector, the camera picker
      (`useListAllCameraChoicesQuery`, mirroring
      `LayoutEditorDialog.tsx:85-98` including the `count`/`complete` truncation
      notice), the Capture button (disabled with no camera or while capturing),
      and the `role="alert"` failure sentence. **Inline styles, matching the rest
      of `OverlayEditor.tsx` — not Tailwind, not tokens** (#2342 owns that).

- [ ] **T009 [US1]** — Wire T006–T008 into `OverlayEditor.tsx`. Depends on
      T005–T008.

- [ ] **T010 [US1]** — `OverlayEditorDialog.tsx`: build a stable `getToken` by
      copying `CameraDetailPage.tsx:20-38`'s ref-behind-`useCallback` shape
      **including its reasoning comment**, and pass it to `<OverlayEditor>`.
      Change nothing else about the form or its payload.

- [ ] **T011 [US1]** — Run T002–T004 green. Then run **T001 unmodified** and
      confirm it is still green. If any T001 assertion needs editing: **stop and
      report** — the behaviour moved (§Testing).

- [ ] **T012 [US1]** — `tsc --noEmit`, `pnpm lint --max-warnings 0` and
      `prettier --check` clean across `apps/shared`, `apps/management-web`,
      `apps/kiosk-web`; full suites green in all three.

---

## Nothing here is behaviour-preserving except the default path

T005–T010 are new behaviour. The single preserved behaviour is *the editor with
no camera chosen*, and T001 is its characterisation. That asymmetry is why the
colour is split rather than picked.

---

## Dependency graph

```
T001 [P] ─┐
T002 [P] ─┤  phase 4a — all four disjoint files, fully parallel
T003 [P] ─┤
T004 [P] ─┘
              │  (verbatim output → engineer)
              ▼
           T005  (foundational: prop surface + backdrop state)
              │
      ┌───────┼───────┐
      ▼       ▼       ▼
   T006[P] T007[P] T008[P]        T010 [P] (dialog — disjoint file)
      └───────┼───────┘             │
              ▼                     │
            T009 ◀──────────────────┘
              │
              ▼
            T011 ──▶ T012
```

**Fan-out for the orchestrator:** T005 is the only true bottleneck. Once it
lands, T006/T007/T008 own disjoint new files and T010 owns a disjoint app file —
four-way parallel. The four phase-4a files are parallel from the start.

---

## Verification (phase 5)

Run `specs/147-a-real-frame-behind-the-canvas/spec.md`
§"Independent end-to-end test procedure", all ten steps, against a booted Aspire
stack. **Steps 5, 6, 7 and 8 are the ones no test can reach** and are therefore
the verification note's substance:

- **step 5** — a real picture appears (proves assumptions 1 and R3 at once);
- **step 6** — the SFU shows no session while the dialog sits open (proves
  FR-014 from the server side, which the tests can only prove from the client's);
- **step 7** — dragging the label between a dark and a bright region visibly
  changes legibility. **This is the issue's entire claim.** If it is not visible,
  the feature did not work regardless of a green suite;
- **step 8** — an offline camera degrades to the checkerboard and does not block
  saving.

Also observe a **non-16:9 camera** if one is available, for FR-019's
letterboxing. If none is, say so rather than claiming it.

**Latency note for the verification:** cite §IV as **N/A — the editor is not on
the event-to-overlay path**, with spec.md's reasoning. Do **not** report a
composite-and-render figure for the editor; that budget belongs to the wall and
quoting an editor number against it would corrupt the record the way §IV's leg
table was corrupted before.

---

## PR notes (phase 7)

- Base `develop`, `--base develop` explicit.
- Quote T001's green output and T002–T004's red output verbatim (ADR-0139).
- State the phase-4a split per artefact.
- Record: **backdrop option 2 + 3 chosen, option 1 rejected**, with the session
  reason.
- Record: **the contrast check is deferred**, and **file the follow-up issue**
  — *"The overlay editor can sample the region behind the label and warn on poor
  contrast"* — referencing this spec and #2340. None of #2341–#2353 is it.
- Record: **the preview camera is not persisted**, citing ADR-0115, ADR-0112 §2
  and spec 004 FR-015.
- Record: **this discharges spec 004 FR-012**'s stretch goal.
- If PR #2354 merged in the meantime, rebase onto `develop` (never stack) and say
  the checkerboard characterisation survived the rebase.
- Closing keyword for #2340, then **check the issue state after the merge** — a
  PR mention auto-closes about one time in three.
- Add the feature issue to Project #13:
  `gh project item-add 13 --owner smartsolutionslab --url <issue-url>`.

---

## Gate — phase 3

Tasks are atomic, each names its story, `[P]` markers reflect disjoint files, and
the one contended file (`OverlayEditor.tsx`, against PR #2354) is called out with
a resolution rule. #2340 is already on Project #13 (status *In Progress*), so the
phase-3 board gate is satisfied — no per-task issues are created (the practice
since spec 028).
