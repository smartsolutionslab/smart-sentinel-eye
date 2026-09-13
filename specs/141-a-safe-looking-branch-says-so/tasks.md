# Spec 141 — Tasks

**Phase:** 3 (Tasks) · **Date:** 2026-09-13
**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Issue:** #2197
**Engineer:** `frontend-engineer`. Every file is `.tsx` under `apps/kiosk-web`;
no backend, no infra, no Aspire, no migration, no `src/` file opened for writing.

**Phase 4a colour — RED, all three sites.** Each site turns a silent (or
misattributed) failure into a reported one, which is new behaviour under
constitution §Testing and ADR-0139. There is no behaviour-preserving task here,
so there is no characterisation lane and no ambiguity to resolve to red.

The behaviour-preserving **claims** (spec FR-006, plan invariants 1-8) are held
down by the **existing** `CellPage.test.tsx` suite, which must pass
**unmodified** — that is T008. **An assertion that has to be edited is a block,
not an adjustment**: it is evidence FR-006 was broken and behaviour moved. This
matters most for the five #2084 cases (`:999-1186`) and the #2069 fab cases,
which sit on the lines this spec edits.

**Every red here is a genuine runtime red**, not a compile error. No new module
and no new export is introduced (plan §"File ownership"), so spec 061's
`24e6fc4c` ruling — that a `TS2305` is not a red test — does not apply and there
is no guards-versus-reds split to record. All eight cases below fail against
today's `CellPage.tsx` with the component rendering and the doubles reached.

**Format:** `[ID] [P?] [Story] description`.

**There is no `[P]` in this spec, and its absence is deliberate rather than an
oversight.** Every task writes either `CellPage.tsx` or `CellPage.test.tsx`, so
under ADR-0109 every task is serial. The parallelism is *across* specs: 141
(`CellPage.tsx`) and #2198's spec (`WhepClient.ts`) own disjoint file sets and
are `[P]` with each other.

---

## Phase 4a — the reds, written and observed failing before any fix

Owner: `test-writer`. All three tasks write
`apps/kiosk-web/src/features/cell/CellPage.test.tsx` and are **serial**.

- **[T001] [US1]** *Foundational for 4a — make the overlay double honour `skip`,
  and answer the collection payload for `''`.*

  `getOverlayMock` (`CellPage.test.tsx:17`, wired at `:32-38`) currently ignores
  its second argument. Extend it so that:
  - called with `{ skip: true }` it answers `{ data: undefined }`;
  - called with `''` and `{ skip: false }` it answers
    `{ data: { chains: [], published: [] } }` — **the real server's answer**, not
    a convenience. Carry the justification in the comment:
    `OverlayEndpoints.cs:59` maps `GET /overlays/` to `List` and declares
    `.Produces<ListOverlaysResponse>(200)`; `GetOne`'s `{overlayIdentifier:guid}`
    constraint cannot match an empty segment; the gateway's
    `/overlay-designer/{**catch-all}` + `PathRemovePrefix` forwards it verbatim;
    and `kiosk-web` / `kiosk-wall` both carry `sse.overlays.read`, so it is
    neither a 404 nor a 403.
  - every existing case keeps the behaviour it has today.

  **This is the reason R2 exists**: `CellPage.test.tsx:123-134` already records
  that a double ignoring `skip` *"cannot tell a skipped query from a fetched
  one"*. Without this task, T002's central red is vacuous.
  **Blocks T002.** Lands with T002 in one commit — a double with no case is
  dead code, and a case against the old double asserts nothing.

- **[T002] [US1]** *Red — site 1: an omitted `overlayIdentifier`.* Five cases.

  1. **The defect.** A 1×1 layout whose tile **omits** `overlayIdentifier`
     (`{ ...tile(), overlayIdentifier: undefined as unknown as string | null }`)
     — spelled with the cast and a comment saying the cast *is* the drift being
     modelled, because `LayoutTile` deliberately does not admit `undefined`
     (spec FR-001). Assert: the page renders without throwing; every
     `getOverlayMock` call carried `{ skip: true }`; and
     `resilienceLines(info.mock.calls, 'tile-without-overlay-identifier')`
     equals `[{ subsystem: 'hub', transition: 'tile-without-overlay-identifier',
     layout: 'cam-1', tiles: 1 }]`.
     **Red today three ways**: the query fires with `skip: false`, the tile
     receives the collection payload and `overlay.revisions.find` throws out of
     `render()`, and no line is emitted.
  2. **Mixed layout.** Two tiles, one omitting and one bound to `ovl-real`. Fire
     an `OverlayHighlightChanged` for `ovl-real` and assert that tile's
     `data-highlighted` is `'true'` — #2197's correction of #2109, asserted
     rather than argued — and exactly one line naming `tiles: 1`.
  3. **The empty string.** A tile whose `overlayIdentifier` is `''`. Same
     assertions as case 1. **Red today**, and it is the second drift the one
     sentinel closes (spec §"Site 1", last paragraph).
  4. **QUIET — the shape today's server always sends.** A tile with
     `overlayIdentifier: null`. Assert **zero** lines of that transition **and**
     — the count assertion that proves the guard ran —
     `expect(getOverlayMock, 'the tile must have rendered and evaluated the
     guard').toHaveBeenCalledWith('', { skip: true })`.
  5. **QUIET — a real identifier still queries.** A tile bound to `ovl-real`.
     Assert zero lines **and** `getOverlayMock` was called with
     `('ovl-real', { skip: false })` — the guard ran and let it through, so a
     sentinel that over-fires fails here rather than passing silently.

  Cases 4 and 5 are FR-008's pair for this site. **A guard deleted so that it
  fires on `null` too must fail exactly case 4**, at its line count — that is
  `spec.md` step 4's counterfactual.
  Depends on **T001**.

- **[T003] [US1]** *Red — site 2: the fab sentinel.* Four cases.

  1. **The defect.** A layout with `fab: undefined as unknown as string`, one
     tile bound to an overlay whose published `text` is `'OEE {{oeeline1}}'`.
     Assert `getSnapshotMock` was called with `{ skip: true }` — **red today**,
     because `fab === ''` is false for `undefined` so the cross-fab request
     fires — and exactly one
     `{ subsystem: 'hub', transition: 'layout-without-fab', layout: 'cam-1' }`.
  2. **The empty string.** `fab: ''`. The snapshot query is **already** skipped
     today, so the `skip` assertion is green; the `[resilience]` assertion is the
     red. Keeps the two halves of the sentinel separately pinned.
  3. **The blind spot, asserted.** With `fab` absent, push a `ResolvedOverlayTextChanged`
     frame that itself carries no fab. Assert the frame is still dropped (the
     label is unchanged), **zero** `resolved-text-without-fab` lines — proving
     `countReportableSkew`'s `wallFab === undefined` early return (`:518`) is
     untouched — and exactly one `layout-without-fab` line, so the wall is no
     longer wholly silent. **Red today**: there is nothing at all.
  4. **QUIET.** `fab: 'munich'`. Assert zero `layout-without-fab` lines **and**
     `getSnapshotMock` was called with `({ overlayIdentifier: 'ovl-1',
     fabId: 'munich' }, { skip: false })` — the positive that proves the guard
     ran and passed.

  Depends on **T002** (same file).

- **[T004] [US1]** *Red — site 3: a resolved-text push for a static label.* Five
  cases, all on a munich wall.

  1. **The defect.** One tile bound to `ovl-drift`, whose published `text` is
     `'OEE [[oeeline1]]'` — the delimiter has moved, the text has not. Assert
     `getSnapshotMock` was called with `{ skip: true }` (the gate ran), push
     `{ overlay: 'ovl-drift', fab: 'munich', resolvedText: 'OEE 99.9',
     version: 2 }` through the existing `pushText` helper (`:1048-1054`), then
     assert the label **still** reads `'OEE [[oeeline1]]'` (FR-006 — the report
     does not fix it) and exactly one
     `{ subsystem: 'hub', transition: 'resolved-text-for-static-label',
     overlay: 'ovl-drift' }`. **Red today: nothing is emitted.**
  2. **The latch.** Push a second frame at version 3. Still exactly one line.
  3. **QUIET — and it proves the handler ran end to end.** Text
     `'OEE {{oeeline1}}'`, same push. Assert the label reads `'OEE 99.9'` — the
     push applied, so execution reached the new guard — and **zero** lines. This
     is the shape of the existing *"Says nothing about the wall's own frame"*
     (`:1177-1186`) and is the case a report-on-every-push implementation fails.
  4. **QUIET — a not-yet is not a fault.** `getOverlayMock` answers
     `{ data: undefined }` for the bound overlay, so `publishedOverlay` is
     undefined. Push. Assert zero lines **and** `getOverlayMock` was called with
     `('ovl-loading', { skip: false })` — the tile rendered, the code ran, and it
     still said nothing. Plan risk R5, and the most likely way to ship a
     false positive.
  5. **CONFLICT — another plant's frame.** Static-labelled overlay, push with
     `fab: 'dresden'`. Zero lines: the report sits behind the existing fab guard,
     so the filter working is never a fault.

  Depends on **T003** (same file).

**Phase 4a hand-off:** T001-T004 are written and run by `test-writer`, and the
**verbatim failing output** is what `frontend-engineer` receives as its brief and
what goes in the PR body. The engineer may not edit these tests to pass
(ADR-0144). The site-1 defect case will surface partly as a thrown `TypeError`
escaping `render()` rather than as a clean assertion failure — **quote it as it
comes out**; that throw is the settled answer to #2197's open question and is the
single most informative line of the red.

---

## Phase 4b — the implementation

Owner: `frontend-engineer`. All four tasks write
`apps/kiosk-web/src/features/cell/CellPage.tsx` and are **serial**.

- **[T005] [US1]** *Foundational — one sentinel, four readers.*
  Add `boundOverlayIn(overlayIdentifier: string | null | undefined): string | null`
  at module scope, answering the identifier when
  `typeof overlayIdentifier === 'string' && overlayIdentifier !== ''`. Replace
  **all four** existing tests: `tilesToBoundOverlays` (`:530`), the `unavailable`
  and `highlighted` props (`:294-295`), and the `Tile` query's `skip` +
  `?? ''` (`:354-355`). Add `namedFab(fab): string | null` on the same shape and
  use it at `:379`.

  The parameter types are deliberately wider than `LayoutTile.overlayIdentifier`
  and `Layout.fab` — match `countReportableSkew(frameFab: string | undefined)`
  at `:515` and say so in one comment. **`layouts.api.ts` is not touched**
  (spec FR-001).

  **Blocks T006 and T007.** It is the only task the other two depend on, and it
  is what makes a fifth reader impossible to get wrong.
  Makes **T002 cases 1, 3, 4, 5** and **T003 cases 1, 2, 4** pass.

- **[T006] [US1]** *Report the two layout-level faults, once per mounted page.*
  A `useRef<Set<string>>` at component scope keyed by transition, and a
  `useEffect` keyed on `[layoutIdentifier, published, data?.fab]` that:
  - counts `published?.tiles` whose `overlayIdentifier` is neither `null` nor a
    non-empty string, and on a non-zero count not already latched emits
    `logResilienceEvent('hub', 'tile-without-overlay-identifier',
    { layout: layoutIdentifier, tiles: count })`;
  - when `namedFab(data?.fab)` is `null` **and** `data !== undefined` — so the
    loading window is not a fault — emits
    `logResilienceEvent('hub', 'layout-without-fab', { layout: layoutIdentifier })`.

  **An effect, not the render body** (plan R4): a `console.info` during render
  doubles under StrictMode, and the latch should be belt and braces rather than
  the only defence. **`countReportableSkew` is not touched** (plan invariant 3).
  Depends on **T005**. Makes **T002 cases 1-3** and **T003 cases 1-3** pass.

- **[T007] [US1]** *Report a resolved-text push for a label the kiosk treats as
  static.* Three pieces:
  - `staticLabelOverlaysRef = useRef<Map<string, boolean>>(new Map())` and a
    stable `useCallback` `onLabelVerdict(overlay, hasPlaceholder)` writing it.
    **A `Map`, not a `Set` of static overlays** — the push handler must be able
    to tell *"the tile said static"* from *"the tile has not decided"* (plan
    §"Where the state lives"; spec FR-005).
  - In `Tile`, a `useEffect` keyed on
    `[overlayIdentifier, hasPlaceholder, onLabelVerdict]` that calls it **only
    when** `publishedOverlay !== undefined && typeof publishedOverlay.text ===
    'string'`. Nothing is written on the loading path or when `text` is absent
    (plan R5).
  - In `onResolvedOverlayTextChanged`, **after** the `boundOverlays` guard
    (`:166`) and **after** `message.fab !== wallFab` (`:197`), **before** the
    version guard (`:199`): when
    `staticLabelOverlaysRef.current.get(message.overlay) === false` and the
    overlay is not already in `reportedStaticPushRef` (a `useRef<Set<string>>`),
    latch it and emit
    `logResilienceEvent('hub', 'resolved-text-for-static-label',
    { overlay: message.overlay })`.

  The snapshot query stays skipped and the label stays as the template
  (FR-006) — this reports the disagreement, it does not resolve it.
  Depends on **T005**. Makes **T004** pass. Writes the same file as T006, so it
  follows rather than runs beside it.

### Phase 4b — the behaviour-preserving guard

- **[T008] [US1]** *The existing suites pass **unmodified**.*
  `apps/kiosk-web`: `CellPage.test.tsx`'s pre-existing cases — in particular the
  five #2084 cases (`:999-1186`) and the #2069 fab cases — plus
  `useWallAlignment.test.ts` and `useLabelDelay.test.ts`. Then `apps/shared` and
  `apps/management-web` in full. Record the counts.
  **An assertion that has to be edited is a block, not an adjustment.** The two
  most likely to move are #2084's `Says nothing about the wall's own frame`
  (which now also exercises the FR-005 guard on a placeholder label and must
  still emit nothing) and any case rendering a `null`-identifier tile (plan
  invariant 1). If either needs editing, T005 or T007 is wrong.
  Depends on **T006, T007**.

- **[T009] [US1]** `pnpm typecheck` (including `e2e/`), `pnpm lint
  --max-warnings 0` and `pnpm format:check` clean.
  One thing to expect rather than be surprised by: `typeof fab !== 'string'`
  narrows a `string` to `never` in the true branch. `tsc` permits this (it is a
  `typeof` narrowing, not a `TS2367` non-overlapping comparison), and
  `@typescript-eslint/no-unnecessary-condition` is **not** enabled —
  `apps/kiosk-web/eslint.config.js:42` composes `tseslint.configs.recommended`,
  not the type-checked set. Verified before writing this task; if it fires
  anyway, that is a finding, not a licence to widen `Layout.fab`.
  Depends on **T008**.

- **[T010] [US1]** Each commit builds on its own (ADR-0087) — verify per commit
  with `tsc --noEmit` on each checked out individually, not at the branch tip.
  Suggested commits: (1) T001+T002, (2) T003, (3) T004, (4) T005, (5) T006,
  (6) T007. Conventional Commits (ADR-0030); **no `Co-Authored-By` footer**
  (ADR-0086).
  Depends on **T009**.

---

## Dependency graph

```
T001 ─> T002 ─> T003 ─> T004          (phase 4a, all one file, serial)
                          │
T005 ─┬─> T006 ───────────┴─> T008 ─> T009 ─> T010
      └─> T007 ──────────────────┘
```

**Parallel markers: none, and the absence is the finding.** T001-T004 all write
`CellPage.test.tsx`; T005-T007 all write `CellPage.tsx`. Under ADR-0109 that is
one owner for the whole spec.

**Fan-out beyond this spec:** 141 (`apps/kiosk-web/.../CellPage.*`) and #2198's
spec (`apps/shared/src/streaming/WhepClient.ts`) own disjoint file sets and are
`[P]` with each other. Neither is blocked on the other, and neither is blocked on
#2109, which is a tracker and stays open until both land.

**The foundational task is T005**, and it blocks only the two implementation
tasks after it. Nothing outside this spec waits on it.

---

## Verification (phase 5)

`pnpm --filter @smart-sentinel-eye/kiosk-web test` is the CI-visible proof.
**Observation** is step 5 of `spec.md`'s end-to-end procedure: a real stack, a
Chrome DevTools Local Override deleting `overlayIdentifier` from one tile of the
layout response, and a wall that stays up and names the field instead of entering
`KioskCrashRecovery`'s reload loop. The *before* half of that observation — the
reload loop itself — is worth capturing once, because it is what settles #2197's
open question against the running system rather than against the route table
(spec assumption A2).

**§IV citation for the PR body:** touches *Event → overlay state* (≤ 200 ms);
**no measurement required** — the change removes one HTTP request on each of two
drift paths and adds one `Map.get` on the push path's drop branch, with no timer,
render, dispatch or actuation added. **No *before* figure is cited**, deliberately:
a figure from a unit test would be a §VII discharge nobody earned. **No cell of
§IV's table moves.**

---

## Gate — phase 3

- Tasks are atomic and each names the file it writes.
- **Phase 4a colour is declared: RED, all three sites**, with the construction
  for each stated in the task and the site-1 red expected to surface as a thrown
  `TypeError` out of `render()`.
- **Every new signal has a named quiet case asserted by a positive** — T002
  cases 4-5, T003 case 4, T004 cases 3-4 — never by an absence alone (FR-008).
- **The board gate is already met:** #2197 is on Project #13 with status
  *In Progress* (verified with `--limit 2000`, not the 30-item default).
  Per-task issues are **not** created — the repo stopped after spec 028.
- **No ADR is written and none is needed** (spec §"The ADR question"). The one
  decision that would need one — Zod at the read boundary, option 1 of #2109 —
  is flagged and stopped, as 095 stopped it. ADR-0144 forbids the lane writing
  it.
- **Two judgements want a human before phase 4:** `subsystem: 'hub'` rather than
  widening `ResilienceSubsystem` with `'layout'`, and the latch rather than
  #2084's decade cadence (FR-007).
