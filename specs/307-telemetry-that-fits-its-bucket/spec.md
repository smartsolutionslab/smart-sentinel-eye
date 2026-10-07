# Spec 307: Telemetry that fits its bucket

**Issue:** #2563 (feature-level). **Found in:** #2221. **Related, not in scope:** #2283 (partition key / per-replica limiter).
**Lane decision (human, on #2563):** throttle/batch kiosk telemetry first. The gateway's rate-limit config and ADR-0106 are **not touched**.
**ADRs:** ADR-0122 (browser measurements enter through a service; a lost report is an accepted missing sample), ADR-0128 (wall alignment, `wall_skew`), ADR-0106 (gateway rate limit, cited only as unchanged), ADR-0075 (RTK Query polling, unchanged), ADR-0109 (disjoint files), ADR-0144 (lane).
**Engineer:** frontend (`apps/shared` + `apps/kiosk-web`). **Phase 4a colour:** red (behaviour-changing: the POST cadence changes), plus a characterisation set that must pass unmodified.

## The problem, confirmed against code (2026-10-07)

A four-tile wall at rest issues, per minute:

| Source | Code | Cadence | Req/min (4 tiles) |
|---|---|---|---|
| Stream status poll | `apps/shared/src/ui/composites/CameraViewer.tsx:135-137` (`pollingInterval: 5000`) | 5 s / tile | 48 |
| `receive_to_decoded` POST | `CameraViewer.tsx:230-267` (`DECODE_SAMPLE_INTERVAL_MS = 5_000`, `:29`; report `:253`) | 5 s / tile | 48 |
| `presentation_buffer` POST | `CameraViewer.tsx:286-353` (`LAG_SAMPLE_INTERVAL_MS = 2_000`, `:66`; report `:332`) | 2 s / tile | 120 |
| `wall_skew` POST | `apps/kiosk-web/src/features/cell/useWallAlignment.ts:153-224` (`SETTLE_INTERVAL_MS = 2_000`, `:35`; report `:221`) | 2 s / wall | 30 |
| **Total** | | | **246** |

The issue's figures hold exactly. All three POSTs go through
`reportKioskLatency` → `send` in `apps/shared/src/observability/kioskLatency.ts:64-124`,
one request per sample, to `POST stream-distribution/streams/kiosk-latency`. The gateway
allows 100/min per partition per replica (2 replicas). Telemetry is 198 of the 246.

## What changes and what does not

- **The samplers keep their cadence.** The decode sampler still reads every 5 s; the lag
  sampler still reads every 2 s and still calls `onLagMeasured` every 2 s; the settle
  loop still runs every 2 s. The alignment **control loop** is a controller, not an
  observer, and its convergence time is untouched.
- **Each sample's meaning is unchanged.** A shipped `presentation_buffer` figure is still
  a 2 s delta window, `receive_to_decoded` still a 5 s window — so dashboard figures
  before and after are comparable. Widening the windows instead was rejected: the code
  itself warns that wider windows flatten the excursion a budget is about
  (`kioskLatency.ts:194-197`, `wallAlignment.ts:156-162`).
- **Only the network send is thinned.** A periodic measurement is POSTed at most once per
  30 s per measurement name per owner (one owner = one mounted tile for the two tile
  legs, the wall for `wall_skew`), leading edge: the first valid sample in a window ships,
  later ones in the window do not. This is systematic sampling of the existing series.
- **The DEV `[latency]` console line stays per sample.** It is the e2e harvest's input
  (spec 108, `e2e/kiosk-shows-a-label-over-video.spec.ts:1213-1221`) and manual
  verification's; it costs no gateway request.
- **Event-driven measurements are untouched:** `overlay_draw`, `label_delay` fire on
  overlay changes, not timers, and cost nothing at rest.
- **The stream status poll is left at 5 s** (assumption A1 below).

## Target

| Source | Before | After |
|---|---|---|
| Stream status poll | 48 | 48 |
| `receive_to_decoded` | 48 | 8 |
| `presentation_buffer` | 120 | 8 |
| `wall_skew` | 30 | 2 |
| **Total (4-tile wall, at rest)** | **246** | **66** |

**−73% total, −91% telemetry.** A wall then fits inside one replica's 100/min with ~34
to spare even if every request lands on one replica, and uses about a third of the
2-replica ~200/min. A `management-web` camera viewer also drops from 24 to 14/min (its
decode sampler goes through the same code). **Success criterion SC-001:** a 4-tile wall
at rest issues ≤ 70 gateway requests per 60 s.

**Not claimed:** that any number of walls behind one NAT now fit. Three walls (≈198/min)
still exhaust a shared bucket. That is the partition problem (#2283, ADR-0106) and stays
out of this slice by the human's decision.

## Assumptions (explicit, review at the gate)

- **A1 — the stream poll stays at 5 s.** It is not telemetry: `useWhepSession.ts:150,359-371`
  turns `Degraded` into "Reconnecting…" and `Degraded→Healthy` into a reconnect, so its
  period is how long an operator waits to see a source failure. Cutting it to 10 s would
  take the wall to 42/min; that is a UX trade the issue did not ask for. Override here if
  wanted — it is one constant.
- **A2 — 30 s.** Gives 2 shipped samples per tile per leg per minute: 40 per leg per
  5-minute window from one 4-tile wall, more across a fab. ADR-0122 already frames these
  as distributions where a missing sample is acceptable.
- **A3 — no batch endpoint.** Batching several figures into one POST would keep every
  sample, but needs a new StreamDistribution contract (backend + `Shared.Contracts`
  change, two engineers). Recorded as the follow-up if 30 s proves too sparse.

## User stories

### US1 (P1) — A wall at rest leaves room in the rate-limit bucket for operator writes

**Why:** a 429 on an operator's variable submit, caused by background telemetry, is
indistinguishable from "nothing happened" (#2563).

**Independent test:** see the end-to-end procedure below.

```gherkin
Scenario: Tile legs ship at most once per 30 s each
  Given a live CameraViewer with a lag callback and a working stats() double
  When 120 s of fake time pass
  Then at most 4 receive_to_decoded POSTs and at most 4 presentation_buffer POSTs were sent
  And onLagMeasured was still called on every 2 s lag sample after the first

Scenario: The wall's skew ships at most once per 30 s
  Given useWallAlignment with three tiles reporting a spread every cycle
  When 120 s of fake time pass
  Then exactly 4 wall_skew POSTs were sent (at 2, 32, 62, 92 s)
  And the target was still recomputed every 2 s cycle

Scenario: The first figure still ships immediately
  Given a newly mounted tile or wall
  When its first valid sample is produced
  Then that sample is POSTed without waiting for a window

Scenario (bad request): An invalid figure does not spend the window
  Given a throttle whose window is open
  When a negative, non-finite or > 60 000 ms figure is reported
  Then nothing is POSTed
  And the next valid figure in the same window is POSTed

Scenario (conflict): Two legs on one tile do not throttle each other
  Given a tile that has just POSTed presentation_buffer
  When its next receive_to_decoded figure arrives within 30 s
  Then receive_to_decoded is POSTed

Scenario: A reconnecting tile does not reopen its window
  Given a tile that POSTed presentation_buffer at t
  When its session drops and returns to live before t + 30 s
  Then no presentation_buffer POST is sent before t + 30 s

Scenario: Console lines stay per sample in DEV
  Given import.meta.env.DEV is true
  When samples are throttled from the network
  Then one [latency] console line is still printed per sample
```

**Auth:** unchanged. The endpoint still requires `sse.streams.read`; a null token still
sends nothing (`kioskLatency.ts:107-108`). Throttling happens before the token is read,
so a throttled sample costs no token resolution.

## End-to-end procedure (phase 5)

1. Boot the stack in run mode (camera-sim present), open a 4-tile kiosk wall, wait for all
   tiles to be Live and 30 s to elapse.
2. In Playwright (`page.on('request')`) or devtools Network, count requests to the gateway
   origin over 60 s: `kiosk-latency` POSTs by `measurement` in the body, and `streams/`
   GETs.
3. Expect ≈ 8 / 8 / 2 latency POSTs and ≈ 48 stream GETs; total ≤ 70 (SC-001). Record the
   before figure from `develop` in the same procedure.
4. Confirm the wall still aligns (no badge regression) and the Aspire dashboard still shows
   the `presentation_buffer`, `receive_to_decoded` and `wall_skew` instruments receiving
   values.

## Latency-budget impact

**N/A for every §IV leg.** Observer-only change; the event→overlay path, the playout
alignment control loop and every sampler cadence are unchanged. Spec 040 FR-012 (the
observer must not consume what it observes) is served better, not worse.

**Measurement-validity note for the PR body (flag, not blocking).** Fewer samples per
tile reach the meters: per-sample semantics are identical, but tail resolution drops —
an excursion confined to a window that is not shipped is not in the histogram. This
matters most for the presentation-buffer leg, which §IV records as *recorded, not yet
observed* (#1714). A p99 read from one wall over a short window will be noisier than
before. No ADR is needed: ADR-0122 accepts missing samples by design and no ADR fixes a
reporting cadence; the cadences were spec-level choices (spec 040, spec 045). If #1714's
observation needs density, A3's batch endpoint is the remedy.

## Out of scope

Gateway rate limit, partition key, replica count, `X-Fab` (ADR-0106, #2283); the dev-mode
AppHost limit (#2221); the stream status poll (A1); a batch endpoint (A3); the event-driven
measurements.
