# Spec 141 — Plan

**Phase:** 2 (Plan) · **Date:** 2026-09-13 · **Spec:** `./spec.md`
**ADRs:** ADR-0074/0075 (the React apps and RTK Query), ADR-0076 (the hub is a
replaceable transport — this change adds no coupling to it), ADR-0079 (Zod stays
on the write path), ADR-0106 (the gateway, and why the collection URL resolves),
ADR-0109 (disjoint files), ADR-0112 §2 (one overlay, several tiles), ADR-0117,
ADR-0139, ADR-0144, ADR-0145 (the wall's fab is derived from its layout).

---

## Bounded context and layers

**No backend context is involved, and no backend file is opened.** This is
entirely browser-side and entirely inside `apps/kiosk-web`. No
`Shared.Contracts` message changes, no DTO changes, no endpoint changes, no
migration — so none of the cross-context boundary rules are engaged and
`NetArchTest` has nothing to say about it.

The backend files the spec cites (`LayoutDto.cs`, `OverlayEndpoints.cs`,
`PlaceholderParser.cs`, `VariableValueChangedDomainEventHandler.cs`,
`ApiGateway/appsettings.json`, the realm JSON) are **read as evidence and not
modified**. That is the whole of their involvement, and it is worth stating,
because the site-1 conclusion depends on them and a later reader will want to
know whether the spec changed what it cited.

| Layer | File | What it owns after this change |
|---|---|---|
| Page composite | `apps/kiosk-web/src/features/cell/CellPage.tsx` | the layout read, the hub subscription, the grid — **and now** the three latches and the three reports |
| Tile composite (same file) | `CellPage.tsx`'s `Tile` | its own overlay + snapshot queries — **and now** reports its label verdict up to the page |
| Reporting channel | `apps/shared/src/observability/resilienceLog.ts` | **unchanged** — three new `transition` strings, no new code, no widened union |
| Seam types | `apps/shared/src/api/layouts.api.ts` | **unchanged** — `overlayIdentifier` stays `string \| null`, `fab` stays `string` |
| Write-path schema | `apps/management-web`'s `layouts.schema.ts` | **unchanged and not read** |

**The design constraint is that `CellPage.tsx` is the only production file that
changes.** Everything the spec needs already exists one import away: the channel
(`logResilienceEvent`, already imported at `:7`), the sentinel shape
(`countReportableSkew`, `:513-525`), the tile→page callback shape
(`onLagMeasured`, `:298`), and the latch convention (`CameraViewer.tsx:137-145`).

---

## Entities and value objects

No domain entities. §II's primitive ban scopes to domain models in `src/`; this
is a browser package. The structures involved are existing TypeScript types plus
one new module-level function and three refs.

| Thing | Where | Invariant |
|---|---|---|
| `LayoutTile` | `layouts.api.ts:15-20` | **unchanged.** `overlayIdentifier: string \| null`, mirroring `TileDto` |
| `Layout.fab` | `layouts.api.ts:44` | **unchanged.** `string`, mirroring `LayoutDto:25` |
| **new:** `boundOverlayIn(overlayIdentifier): string \| null` | `CellPage.tsx`, module scope | answers the identifier when `typeof … === 'string' && … !== ''`; `null` otherwise. **Parameter typed `string \| null \| undefined`** — deliberately wider than the DTO, exactly as `countReportableSkew(frameFab: string \| undefined)` is wider than `message.fab: string` |
| **new:** `namedFab(fab): string \| null` | `CellPage.tsx`, module scope | the same test for the wall's fab: `typeof fab !== 'string' \|\| fab === ''` ⇒ `null` |
| **new:** `reportedLayoutFaultsRef` | `CellPage` component scope | `useRef<Set<string>>` keyed by transition — one entry per report kind, so FR-002 and FR-004 each fire once per mounted page |
| **new:** `staticLabelOverlaysRef` | `CellPage` component scope | `useRef<Map<string, boolean>>` — overlay identifier → "this tile found a `{{`". Written by tiles, read by the push handler |
| **new:** `reportedStaticPushRef` | `CellPage` component scope | `useRef<Set<string>>` keyed by overlay — FR-005's per-overlay latch |

**Why two helpers rather than one generic `nonEmptyString`.** They read
different things for different reasons and their call sites want different
answers: `boundOverlayIn` answers an *identifier or nothing* and is consumed as a
value four times; `namedFab` answers the same shape but exists to make one `skip`
expression legible. A single generic predicate would be marginally shorter and
would lose the two names that say what is being tested. Recorded so review does
not re-litigate it; collapsing them is a nit either way.

**Why `boundOverlayIn` takes the identifier rather than the tile.** Two of its
four callers (`:294-295`) already have `cell.tile` and two (`:355`, `:530`) have
the identifier in hand. Taking the narrower argument keeps it a pure string
predicate with no dependency on `LayoutTile`'s shape, so a later change to the
tile type cannot silently change what "bound" means.

---

## Invariants the change must preserve

Stated as invariants because they are what the existing suite holds down, and an
assertion that has to be edited is a block rather than an adjustment:

1. **A `null` overlay identifier behaves exactly as today** — unbound tile, no
   overlay query, no snapshot query, no report. The healthy shape must not become
   a fault (FR-008's first quiet case).
2. **A mixed layout keeps matching pushes for its real overlays** (#2197's
   correction of #2109, now asserted rather than argued).
3. **`countReportableSkew` is byte-identical after the change**, and its five
   existing cases pass unmodified. Its `wallFab === undefined` early return
   (`:518`) is not touched.
4. **The fab filter itself does not change.** A frame naming another plant is
   still dropped, still silently, and a fab-less frame is still reported under
   `resolved-text-without-fab` at the decade cadence.
5. **The snapshot cache key stays one object shape shared by the push
   (`:210-220`) and the query (`:377-380`).** Spec 067's Risk 1: two copies that
   drift leave the tile silently stale rather than wrong. Nothing in this change
   touches either, and the existing test's `useSnapshotFromTheRealCache`
   (`CellPage.test.tsx:123-134`) is what keeps them honest.
6. **A static label still renders its raw template and still issues no snapshot
   request** (FR-006). The report is observational.
7. **No render is provoked by any new state.** Three refs, no `useState`, and the
   tile's new effect writes a ref only.
8. **`LiveUpdatesBadge` is unaffected** — a DTO skew is not a hub outage.

---

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change, no
RabbitMQ, no new hub callback. `useLayoutLifecycle`'s callback set is unchanged.

The only outward signal is `console.info` through `logResilienceEvent`, whose
`[resilience]` prefix and `{subsystem, transition, ...detail}` shape are a stable
observable contract (`resilienceLog.ts:3-14`). Three new `transition` values:

| Transition | Subsystem | Detail | Emitted from | Cadence |
|---|---|---|---|---|
| `tile-without-overlay-identifier` | `hub` | `{ layout, tiles }` | `CellPage` render path, over `published.tiles` | once per mounted page |
| `layout-without-fab` | `hub` | `{ layout }` | `CellPage` render path | once per mounted page |
| `resolved-text-for-static-label` | `hub` | `{ overlay }` | `onResolvedOverlayTextChanged`, after the bound and fab guards | once per overlay per mounted page |

`subsystem: 'hub'` rather than a new `'layout'`: the reasoning, and the
alternative, are in `spec.md` §"Locked tech choices". It is the one call a
reviewer might reverse, and reversing it is three string literals and one union
member.

`detail.layout` is the `layoutIdentifier` route param, matching `detail.overlay`
in #2084's two lines (`:195`, `:231`) — a noun named after the thing, not
`layoutIdentifier`, because this file's existing `[resilience]` details already
spell it that way and a stable contract must not carry two spellings.

---

## Where each report is decided, and why there

Three decisions, and getting any of them wrong turns a signal into noise.

### FR-002 — during render, over the published revision

`published.tiles` is in hand at `:267` where `buildGridCells` is called. The
count of affected tiles is one pass over the same array `tilesToBoundOverlays`
already walks. Reporting here rather than inside `Tile` is what makes it one line
for a 5×5 wall rather than twenty-five.

**Rendering is not the place to log**, and this is the one wrinkle. React may
render a component twice (StrictMode in dev, or a concurrent replay) and a
`console.info` in the render body would double. The latch makes that harmless —
the `Set` is checked before the write — but the report is placed in a
`useEffect` keyed on `[layoutIdentifier, published]` rather than in the render
body, so it is a side effect where side effects belong. The latch stays anyway:
belt and braces, and it is what makes "once per session" true across a refetch.

### FR-004 — the same effect, the same latch set

`data?.fab` is the wall's fab and there is exactly one of it. Same effect, second
condition, second key in the same `Set`.

### FR-005 — in the push handler, after the two guards that already exist

Placed **after** `boundOverlays.has` (`:166`) and **after** `message.fab !==
wallFab` (`:197`), and **before** the version guard (`:199`) — a push that loses
the version race is still evidence of the disagreement, and dropping it there
would make the report depend on frame ordering.

The tile's verdict reaches the page through `staticLabelOverlaysRef`, written by
a `useEffect` in `Tile` keyed on `[overlayIdentifier, hasPlaceholder,
onLabelVerdict]`. The callback is a `useCallback` with an empty dependency list
writing a ref — stable, so the effect does not re-run per page render.

**This is the existing `onLagMeasured` shape** (`:298`, spec 045): a tile hands
the page a fact only the page can act on, through a stable callback, into a ref.
Not a new pattern.

---

## Where the state lives, and the three candidates that were rejected

Same fork spec 095's plan took, same answer, restated because this file has its
own reasons:

- **Module-level `Set`** — survives unmount, so a kiosk that navigates back to
  the picker and into the same wall never reports again, and vitest cases leak
  state into each other. **Rejected.**
- **`useState`** — a write re-renders the whole grid, which is a wall of live
  `<video>` elements, to record something nobody displays. This is the reason
  `useWallAlignment` holds lags in a ref and the reason `CameraViewer`'s latches
  are refs. **Rejected.**
- **A ref inside the effect closure** — the layout effect is keyed on
  `[layoutIdentifier, published]`, so a republished layout resets it and a wall
  that republishes hourly reports hourly. **Rejected.**
- **`useRef` at component scope** — reset on remount, invisible to render, no
  re-render. **Chosen**, for all three.

`staticLabelOverlaysRef` is a `Map`, not a `Set`, and deliberately: it records
*what the tile decided*, including `true`, so the push handler tests
`map.get(overlay) === false` rather than `!map.has(overlay)`. The difference is
the not-yet case — an overlay whose query has not resolved is **absent** from the
map, and absent must not read as "static" (spec FR-005). A `Set` of static
overlays would conflate the two and re-introduce exactly the defect class the
spec exists to close.

Entries are never deleted. Two tiles bound to one overlay write the same value
(spec A3), and a stale entry for an overlay no longer on the wall cannot be
reached because `boundOverlays.has` filters first.

---

## Boundary rules

- **No cross-context project references** — N/A; nothing in `src/` is touched.
- **`apps/kiosk-web` may import from `apps/shared`, never the reverse.**
  Unchanged: the new code is in `apps/kiosk-web` and imports only
  `logResilienceEvent`, already imported at `CellPage.tsx:7`.
- **`apps/shared` is not written at all.** `resilienceLog.ts` is a contention
  file — 095 declined to touch it and recorded a finding it deliberately left
  alone there. This spec does the same.
- **`apps/management-web` is not read or written.** `layouts.schema.ts` is its
  file, and it is the write path.

---

## File ownership and collisions (ADR-0109)

| File | 141 | Claimed by |
|---|---|---|
| `apps/kiosk-web/src/features/cell/CellPage.tsx` | **writes** | last written by spec 095's era; **explicitly left alone by spec 095** ("`CellPage.tsx` (096)") |
| `apps/kiosk-web/src/features/cell/CellPage.test.tsx` | **writes** | same |
| `apps/shared/src/observability/resilienceLog.ts` | never | spec 011; 095 recorded a finding rather than editing it |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | never | specs 095, 140 |
| `apps/shared/src/ui/composites/useWhepSession.ts` | never | spec 094, #2157 (`agent:blocked`) |
| `apps/shared/src/streaming/WhepClient.ts` | never | #2198 (part 3) |
| `apps/shared/src/api/*.ts` | never | — |
| `src/**` | never | — |

**Two production files, and only one of them is production.** The whole change is
`CellPage.tsx` plus its suite.

**Fan-out:** 141 (`CellPage.tsx`) and #2198's spec (`WhepClient.ts`) own disjoint
file sets and are `[P]` with each other — two frontend engineers can run at once.
**Inside 141 there is no parallelism**: every task writes one of two files, so
every task is serial. `tasks.md` carries no `[P]` marker and says why rather than
leaving its absence to be read as an oversight.

---

## Risks

**R1 — the quiet cases pass because the guard never ran.** The failure mode 095's
review found, and the reason FR-008 exists. In this file it is sharper than in
095's, because a `CellPage` test can render, assert "no `[resilience]` line", and
be green while the component threw, or never mounted a tile, or mounted it with
`skip: true` for an unrelated reason. **Every quiet case asserts a positive**: a
`getOverlayMock`/`getSnapshotMock` call count and its `skip` argument, or a
rendered label value. Named per case in `tasks.md`. The counterfactual in
`spec.md` step 4 is how it is proved rather than claimed.

**R2 — the overlay double must honour `skip`, or half the reds are vacuous.**
`CellPage.test.tsx` already learned this for the snapshot hook and says so at
`:123-134`: *"a fake that ignores it cannot tell a skipped query from a fetched
one — which is what made the placeholder comment below a claim about production
rather than about this test."* `getOverlayMock` (`:17`) currently ignores `skip`.
Site 1's red — the tile must not receive the collection payload — is only
meaningful against a double that returns `{ data: undefined }` when skipped.
**T001 extends the double before it writes the case**, and the extension must not
change what the existing cases see.

**R3 — the site-1 fixture is an invention unless it is justified.** The double
answers `{ chains: [], published: [] }` for the `''` key. That number is not
chosen for convenience: it is what `OverlayEndpoints.cs:59` + the YARP catch-all
+ the kiosk's `sse.overlays.read` scope produce, and `spec.md` §"Site 1" carries
the chain. The test comment must cite it, or the next reader will take the shape
for a guess.

**R4 — reporting from the render body would double under StrictMode.** Mitigated
by putting FR-002 and FR-004 in an effect and by the latch. Called out because
the natural place to write the check is next to `buildGridCells` at `:267`.

**R5 — the `Tile` effect could fire on the not-yet path.** `publishedOverlay` is
`undefined` until the overlay query resolves, and `hasPlaceholder` is `false`
then — the same value a genuinely static label produces. If the effect writes
`false` in that window, a push arriving early is reported as a fault. The
condition is `publishedOverlay !== undefined && typeof publishedOverlay.text ===
'string'`, and the *not-yet* case in `tasks.md` is what pins it. This is spec
095's FR-002 one layer up, and it is the single most likely way to ship a
false-positive here.

**R6 — `CellPage.tsx` grows past 600 lines.** No ESLint `max-lines` rule is
configured for the apps (`apps/kiosk-web/eslint.config.js` composes
`tseslint.configs.recommended` only, not the type-checked set), and ADR-0084's
300-LOC limit is SonarAnalyzer on C#. So nothing fails — but a 630-line React
file is a real cost, and the honest statement is that this spec adds to it
deliberately rather than extracting a module for three latches. Extracting
`CellPage`'s hub handlers into a hook is a worthwhile follow-up and is **not**
mixed into a behaviour-changing spec.

**R7 — an unverified premise driving a design.** Spec assumption A1: no field
here has ever been observed absent, and today's server cannot omit them. The
mitigation is that the change costs three branches and one effect if A1 is never
true, and that `spec.md` records A1 so a later reader does not treat this work as
evidence of drift.

---

## Gate — phase 2

The plan introduces no new dependency, no new abstraction, no new file, no new
union member and no ADR. It reuses `logResilienceEvent` (six existing call sites),
#2084's sentinel from twenty lines away in the same file, 095's component-scope
ref latch, and spec 045's `onLagMeasured` tile→page callback shape. Constitution
§IV impact is stated in `spec.md` and moves no cell of the leg table. ADR-0109
ownership is two files, both already declared unclaimed by spec 095.
