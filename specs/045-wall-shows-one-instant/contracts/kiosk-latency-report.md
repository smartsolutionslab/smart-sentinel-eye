# Contract change: `KioskLatencyReport` gains two measurement names

**Endpoint**: `POST /stream-distribution/streams/kiosk-latency`
**Scope**: `Scope.Sse.Streams.Read` · **Auth**: browser-kiosk principals only
**Changed by**: spec 045 (US2 / FR-009)

## Why this is a contract change and not an addition

`RecordKioskLatency` maps `Measurement` through a **closed set**:

```csharp
LatencySegment? segment = report.Measurement switch
{
    "overlay_draw"       => LatencySegment.KioskOverlayDraw,
    "receive_to_decoded" => LatencySegment.KioskReceiveToDecoded,
    _                    => null,
};
// null => 400 ValidationProblem: "must be 'overlay_draw' or 'receive_to_decoded'"
```

An unrecognised name is **refused with a 400**, not ignored. So a kiosk that
starts sending a new name before the server knows it posts every measurement
into a validation error — silently, because `reportKioskLatency` deliberately
swallows failures so an observability fault cannot break a wall.

**Therefore client and server change in one commit.** Splitting them produces a
kiosk that looks healthy and reports nothing.

## Request (unchanged shape)

```jsonc
{
  "measurement": "presentation_buffer",  // was: overlay_draw | receive_to_decoded
  "camera": "0198f2c1-...",              // tile's camera; Guid.Empty is refused
  "elapsedMilliseconds": 42.7            // the figure already computed; never a start
}
```

## Added values

| `measurement` | Quantity | Recorded as | Budget |
|---|---|---|---|
| `presentation_buffer` | Delay this leg added to the tile — **achieved, not the setpoint** | `LatencySegment.PresentationBuffer`, `isWholeLeg: true` | 200 ms |
| `wall_skew` | Spread between most- and least-lagged held tile on the wall | **Its own instrument** — see below | none |

### `presentation_buffer` — `isWholeLeg: true`

Unusual, and earned. The kiosk both *causes* the delay and *observes* it, so
unlike `receive_to_decoded` nothing is missing from the number and the 200 ms
budget applies directly.

### `wall_skew` does not go through `LatencyBudget`

`LatencyBudget.Record` records *how long a named segment took*. **A skew is a
spread between two tiles, not a journey any frame made.** Recording it as a
latency segment would file a number under a name meaning something else — the
failure `isWholeLeg`, `"in part"` and `"recorded, not yet readable"` all exist
in this codebase to prevent.

It needs its own instrument. The transport is shared because the guards,
the auth gate and the report-don't-export rule (ADR-0122) are all the same;
only the sink differs.

**Consequence for the endpoint**: `Measurement` no longer maps one-to-one onto
`LatencySegment`. The switch must route two ways, and the "must be one of…"
validation message must list all four names.

## Unchanged, and must stay so

- **Elapsed, never a start.** A slow or retried post makes the report late; it
  can never make the measurement large.
- **Untrusted input** (§VIII). The browser applies the same guards; the server
  *enforces* them. Non-finite is refused; negative and absurd are dropped inside
  the recorder so a second caller cannot forget the reason.
- **Non-kiosk principals are accepted and dropped** via `IsBrowserKiosk()`
  (#1893). management-web mounts the same `CameraViewer` and must not land
  desktop figures in kiosk-named series. It never runs the wall controller, so
  it should never send these two — the gate is the backstop, not the design.
- **202 Accepted, never 200.** Nothing is read back and no caller waits.

## Test obligations

1. Each of the two new names is accepted and records to its own instrument.
2. An unknown name is still refused, and the message names all four.
3. `presentation_buffer` carries `isWholeLeg: true`; `receive_to_decoded` still
   carries `false`.
4. A non-kiosk principal sending either new name is accepted and dropped.
5. `Guid.Empty` camera and non-finite elapsed are still refused.
