# Plan 228 — What the frontend review left

**Spec**: [spec.md](./spec.md) · **Issue**: #2306 · **Phase**: 2 (Plan)
**Engineer**: `frontend-engineer` · **Test author (4a)**: `test-writer`

## 1. Shape

Frontend only. No bounded context, no domain model, no `Shared.Contracts`, no
messaging, no AppHost change, no new runtime resource, no new dependency. Constitution
§II and §III do not bind (no C#, no cross-context reference). The "layers" here are the
three frontend workspaces (ADR-0074): `apps/shared` (composites, observability),
`apps/management-web` (the console), `apps/kiosk-web` (the wall).

Five independent edits (items 2-6 of #2306), one PR, one commit per item. Item 1 is
deferred to #2342 (spec §2).

## 2. Constitution / ADR alignment

| Check | Result |
|---|---|
| ADR-0077 primitives | Item 2 uses native radios, as `BackdropControls.tsx` already does. Adding `@radix-ui/react-radio-group` was considered and rejected: a dependency for behaviour the platform supplies, and a second radio idiom in the repo. |
| ADR-0078 / ADR-0148 tokens | Item 2's classes stay on existing Tailwind token utilities (`border-accent-active`, `text-fg-muted`, …). No literal colour is added anywhere. Item 1 untouched (#2342). |
| #2346 live-region lesson | Item 3's region is always mounted and its text changes; never inserted with content. |
| §IV latency | No leg changed (spec §8). |
| §Testing | Two colours, sequenced (spec §6). |
| ADR-0036 | No abstraction introduced. `cameraName` has a real consumer (`CameraDetailPage`). |
| House rules | `useId()` for the radio `name` (a hard-coded name cross-wires two mounted instances, per `BackdropControls.tsx:63-66`). No drive-by comments; the one rewritten comment (`kioskLatency.ts:81-86`) states a *why*. |

## 3. Per-item design

### Item 2 — `GridDesigner.tsx:198-226`

Replace the `div role="radiogroup"` + `button role="radio"` block with:

```tsx
const presetGroupName = useId();
...
<fieldset className="flex flex-col gap-2">
  <legend …>Grid size</legend>
  <div className="flex gap-2">
    {GRID_PRESETS.map((preset) => {
      const active = …;
      return (
        <label key={preset.label} className={active ? ACTIVE_PILL : IDLE_PILL /* + has-[:focus-visible]:ring-2 */}>
          <input type="radio" className="sr-only" name={presetGroupName}
                 value={preset.label} checked={active}
                 onChange={() => selectPreset(preset.rows, preset.cols)} />
          {preset.label}
        </label>
      );
    })}
  </div>
  {gridError …unchanged}
</fieldset>
```

- The `<fieldset>` + `<legend>` already name the group; no `aria-label` needed.
- Visual: the existing pill class strings move to the `<label>`. Focus visibility via
  Tailwind 4.3's `has-[:focus-visible]:` variant on the label (the input is `sr-only`).
  The engineer may choose another technique provided FR-003 holds.
- `onChange`, not `onClick`: arrow keys fire `change` on the newly checked radio, which is
  how the selection follows the keyboard.
- `selectPreset` is unchanged.

### Item 3 — `CameraViewer.tsx:378-423`, `CameraDetailPage.tsx:147`

- Hoist the label derivation out of `ViewerOverlay` into a small pure function
  `announcementFor(status, stream, queryError): string` (returns `''` for `live`,
  else exactly the label `ViewerOverlay` paints). `ViewerOverlay` consumes the same
  function for its visible label so the two cannot drift.
- Root `div` gains, **unconditionally**:
  `<p role="status" data-testid="camera-viewer-status" className="sr-only">{announcementFor(…)}</p>`
- `ViewerOverlay`'s root `div` gains `aria-hidden="true"`.
- `<video … aria-label={cameraName === undefined ? 'Live camera video' : `Live video: ${cameraName}`} />`
- `CameraViewerProps.cameraName?: string` with a one-line doc comment ("names the
  video for assistive technology").
- `CameraDetailPage.tsx:147`: `<CameraViewer … cameraName={record.name} />`.
- Repeated identical text (#2344) is not a concern: status transitions always change the
  string (`live` → '' in between), and a repeat of the same non-live state without an
  intervening change is not a new event.

### Item 4 — `kioskLatency.ts:81-87`

```ts
if (import.meta.env.DEV) {
  console.info('[latency]', { measurement, camera, elapsedMilliseconds });
}
void send(…);
```

Comment at `:81-86` rewritten: the line is for manual verification and spec 108/225's
e2e harvest, both of which run under `vite dev`; a production wall is never restarted,
so an unconditional line per sample is retained console buffer forever.

### Item 5 — tests only

- `useWallAlignment.test.ts`: delete `:380`. Add, after `:381`:
  "Reports no frame age for a tile that reported and then aged out" — `useWallAlignment(3)`
  (or 2 with a survivor), report `departing` at 150/70 plus survivors, `cycle()`, assert
  `frameAgeFor('departing')` is 150 (**the precondition, observed**), then keep only the
  survivors reporting for > 15 s of cycles (mirror `:211-217`), assert
  `frameAgeFor('departing')` is `null`.
  The intermediate `toBe(150)` is what makes the final `toBeNull()` mean "aged out"
  rather than "never reported".
- `labelDelay.test.ts`: delete `:32`.

### Item 6 — `wallAlignment.ts:416-438`

Move `const trial = wallTargetFrom([...stillHeld, ...markedWithLags]);` to just after
`markedWithLags` (`:419`), before the loop. The loop body keeps
`const wouldHold = trial !== null && trial.held.includes(camera);`. Nothing else moves.

## 4. Verification evidence (what phase 4/5 must capture)

| Item | Red / counterfactual | Green |
|---|---|---|
| 2 | `GridDesignerKeyboard.test.tsx` red on buttons (tab visits all four; ArrowRight does nothing) | green after; `LayoutEditorDialog.test.tsx` unmodified and green |
| 3 | `CameraViewerAnnouncement.test.tsx` red (no `camera-viewer-status`; video unnamed); the `CameraDetailPage` test red (no `cameraName` passed) | green after; existing `CameraViewer*.test.tsx` unmodified and green |
| 4 | new `kioskLatency.test.ts` case with `vi.stubEnv('DEV', false)` red | green after; existing `:97` case unmodified and green |
| 5 | counterfactual: remove `useWallAlignment.ts:145` delete → new test red; restored | green |
| 6 | counterfactual: `trial = null` → marked-tile recovery tests red; restored | `wallAlignment.test.ts` green before and after, byte-unmodified |

Phase 5 additionally: the manual procedure in spec §4 steps 2-4, and the three e2e specs
in step 5 (`camera-detail`, `layouts`, `kiosk-shows-a-label-over-video`).

## 5. Risks

- **user-event radio support** (spec A4). Fallback is a Playwright keyboard assertion in
  `e2e/layouts.spec.ts`; the requirement is not dropped.
- **Strict-mode `getByRole('status')` in e2e** (spec A3). Analysed safe; confirmed by
  phase 5 running the specs.
- **`vi.stubEnv` and `import.meta.env.DEV`**: Vitest 4 supports stubbing `import.meta.env`
  keys; call `vi.unstubAllEnvs()` in `afterEach` so the stub cannot leak into the `:97` case.

## 6. Commit plan (ADR-0030, each commit builds on its own)

1. `refactor(observability): compute the wall trial target once per settle cycle` (item 6)
2. `test(observability): assert a tile that aged out, not one that never reported` (item 5)
3. `fix(observability): log latency samples to the console in development only` (item 4, test + code)
4. `fix(layouts): make the grid-size picker a native radio group` (item 2, test + code)
5. `fix(viewer): announce stream-state changes and name the video` (item 3, test + code)

A red test and its implementation share a commit (the new-prop test for item 3 would not
typecheck alone); the red output is quoted in the PR body instead.
