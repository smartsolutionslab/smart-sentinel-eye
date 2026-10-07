# Plan 307: Telemetry that fits its bucket

Spec: [spec.md](spec.md) · Issue #2563 · ADR-0122, ADR-0128 (ADR-0106 untouched).

## Constitution / ADR check

| Rule | Status |
|---|---|
| ADR-0106 gateway rate limit | Untouched. No file under `src/ApiGateway`, no AppHost change. |
| ADR-0122 browser measurements via a service | Same endpoint, same body, same guards; fewer sends. Its "lost report = missing sample" consequence is what makes decimation legitimate. |
| ADR-0128 alignment | Control loop untouched (`onLagMeasured`, settle cycle, deadband). Only the `wall_skew` *report* is thinned. |
| §IV latency budget | N/A — observer only (spec §Latency). |
| §VII dashboard for implemented legs | Still recorded; instruments still receive values. |
| Bounded contexts / backend | None touched. No contract, no migration. |
| New ADR needed | **No.** No ADR fixes a reporting cadence; ADR-0106 is not amended. |

## Where it lives

All in `apps/shared/src/observability/kioskLatency.ts` plus two call sites. No new file, no
new utility package: there is **no existing throttle/debounce utility** in `apps/shared` or
`apps/kiosk-web` to reuse — the only debounces are local timers in the overlay editor
(`OverlayDraftFormResolvePreview`, `TEXT_IDLE_MS`), which delay a trailing call and are the
wrong shape (we want leading-edge admission, no timer). The nearest precedent is the
ref-held, component-scoped counters in `CameraViewer.tsx:175-217` (`countReportableFailure`,
decade cadence), whose scoping reasoning this plan reuses.

## Design

### 1. `kioskLatency.ts` — a leading-edge throttle, owned by the caller

```ts
export const PERIODIC_REPORT_INTERVAL_MS = 30_000;
export type ReportThrottle = (measurement: KioskMeasurement) => boolean;
export function createReportThrottle(intervalMs: number = PERIODIC_REPORT_INTERVAL_MS): ReportThrottle;

export function reportKioskLatency(
  measurement, camera, elapsedMilliseconds, getToken,
  throttle?: ReportThrottle,   // new, optional, last
): void;
```

- `createReportThrottle` closes over a `Map<KioskMeasurement, number>` of last-admitted
  `performance.now()` (never `Date.now()` — PTP-stepped clocks, the rule at
  `kioskLatency.ts:138-141`). Admits when no entry, or `now - last >= intervalMs`, then
  records `now`. Keyed **by measurement**, so one tile's two legs never throttle each
  other.
- `reportKioskLatency` order: **guards → DEV console line → throttle → send.** Guards first
  so a rejected figure cannot spend the window; console before the throttle so the DEV
  line stays per sample (spec 108 harvest, ADR-0122 Option C "alongside").
- `throttle` absent ⇒ today's behaviour exactly (every existing caller and test that does
  not pass one is unchanged — `overlay_draw`, `label_delay`).

**Why caller-owned, not a module-level map:** module state survives across tests in a file
and vitest's fake `performance.now()` restarts at 0 per `useFakeTimers()`, so a global
"last sent" would silently throttle the next test's first report. Caller-owned state also
gives the scoping the spec wants (per tile, per wall) for free.

### 2. `CameraViewer.tsx` — one throttle per mounted tile

- `const reportThrottleRef = useRef<ReportThrottle>(createReportThrottle())` — or lazily
  initialised — at **component scope**, beside `reportedMissingFieldsRef` (`:175`), for the
  same reason that ref gives: both sampler effects are keyed on `status`, so a throttle
  inside an effect would reopen its window on every reconnect and a flapping tile would
  POST on each recovery.
- Pass `reportThrottleRef.current` as the 5th argument at `:253` (`receive_to_decoded`) and
  `:332` (`presentation_buffer`). Nothing else in either effect changes; `onLagMeasured`
  (`:320`) is not gated.
- Not in either effect's dependency array (a ref).
- Camera swap at a grid position does not reset it — accepted, same reasoning as
  `:201-207`: worst case the new camera's first figure waits ≤ 30 s.

### 3. `useWallAlignment.ts` — one throttle per wall

- `const skewThrottleRef = useRef(createReportThrottle())` beside `getTokenRef` (`:93`).
- Pass it at `:221`. Settle cadence, `setTarget`, deadband untouched.
- Keyed by measurement (`wall_skew`), not by camera, so a changing laggiest tile cannot
  multiply the rate: exactly ≤ 2/min per wall.

### Arithmetic (leading edge, ≥ 30 000 ms)

- `presentation_buffer`: lag ticks every 2 s, first delta at 4 s → ships at 4, 34, 64, 94 s.
- `receive_to_decoded`: decode ticks every 5 s, first delta at 10 s → 10, 40, 70, 100 s.
- `wall_skew`: settle every 2 s, first at 2 s → 2, 32, 62, 92 s.
- 120 s ⇒ 4 / 4 / 4 per owner; per minute 2 / 2 / 2 ⇒ 4-tile wall 8 + 8 + 2 = 18 telemetry.

## Testing (phase 4a)

**Colour: red** for the new behaviour; **characterisation** for everything else, which must
pass **unmodified**.

How "requests per minute" is measured in a unit test: `vi.useFakeTimers()` (confirm the
project config fakes `performance` — `useWallAlignment.test.ts`'s staleness tests already
depend on it), `vi.stubGlobal('fetch', vi.fn(...))` recording parsed bodies (the
`capturingKioskLatency` helper, `useWallAlignment.test.ts:33-44`), `await
vi.advanceTimersByTimeAsync(120_000)`, then count bodies by `measurement`. For
`CameraViewer`, reuse the `steppedStatsDouble` / session-double setup in
`CameraViewerSamplerWindow.test.tsx:152-210` (mocked `useGetStreamQuery`, so the RTK poll
is not in the count — it is unchanged and out of scope). Exact expected counts should be
derived from the double's first-valid-delta time; the plan's 4/4/4 assumes the first delta
at one tick after seeding.

Red tests (new):

1. `apps/shared/src/observability/kioskLatency.test.ts` — `createReportThrottle`: admits the
   first; refuses at +29 999; admits at +30 000; measurements independent. `reportKioskLatency`
   with a throttle: a refused sample sends no fetch but **does** print the DEV `[latency]`
   line; a negative / NaN / 60 001 figure does not consume the window (next valid one in
   the window is sent); a throttled call never invokes `getToken`.
2. New `apps/shared/src/ui/composites/CameraViewerReportCadence.test.tsx` — live tile with
   `onLagMeasured`, 120 s: `presentation_buffer` POSTs = 4, `receive_to_decoded` POSTs = 4
   (red today: ~59 and ~23); `onLagMeasured` call count unchanged from an un-throttled
   expectation (~59); console `[latency]` lines still per sample. Plus: status
   `live → reconnecting → live` within 30 s of a POST does not produce a second POST in
   that window.
3. `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts` — new case: three tiles
   re-reporting every cycle, 120 s ⇒ `wall_skew` POSTs = 4 (red today: 60), and a target
   change still lands within one cycle.

Characterisation (run before, must pass unmodified after): `kioskLatency.test.ts`
(existing cases), `CameraViewer*.test.tsx` (in particular `CameraViewerSamplerWindow` and
`CameraViewerAlignment`, which read console lines and `onLagMeasured`, not POSTs),
`useWallAlignment.test.ts` (existing cases: `:82`, `:190-192` assert a single leading-edge
report, `:151`, `:311`, `:333` assert none / no increase — all hold under a leading edge),
`LayoutGrid*.test.tsx`, `CellPage*.test.tsx`, management-web `CameraViewer*.test.tsx`. An
assertion that would need editing is evidence the behaviour moved: block, do not adjust.

New test file ⇒ add it to the vitest shard filter if one applies to `apps/shared`
(memory: new test classes need a shard-filter entry — check whether frontend shards exist
before assuming).

## Files

| File | Change |
|---|---|
| `apps/shared/src/observability/kioskLatency.ts` | throttle factory, constant, optional param |
| `apps/shared/src/observability/kioskLatency.test.ts` | red cases |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | ref + two call-site args |
| `apps/shared/src/ui/composites/CameraViewerReportCadence.test.tsx` | new, red |
| `apps/kiosk-web/src/features/cell/useWallAlignment.ts` | ref + one call-site arg |
| `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts` | one red case |
