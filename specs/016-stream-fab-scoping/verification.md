# Verification: Fab-scope stream distribution

**T028** — [quickstart.md](./quickstart.md) walked end to end. "Done" is the
observations, so they are here rather than a tick.

Everything below was observed against the real Aspire stack (Postgres,
Keycloak, RabbitMQ, MediaMTX) on 2026-08-16, at commit `2ffcc0b`.

## What was observed, and how

| Quickstart step | Observed by | Result |
|---|---|---|
| 1. Attribution over blanked fabs | `StreamFabAttributionIntegrationTests` (2 cases) | ✅ **with one caveat, below** |
| 2. The window fails closed | `A_stream_with_no_fab_is_returned_to_nobody` | ✅ |
| 3. The scoped reads | `StreamFabScopingIntegrationTests` (5 cases) | ✅ |
| 4. The callback is untouched | `WhepAuthIntegrationTests` (5) + `WhepHandshakeLatencyTests` | ✅ unchanged |
| 5. A new camera lands in its own fab | `StreamFabDerivationIntegrationTests` | ✅ |

25 of 25 `Integration.Tests.StreamDistribution` cases pass.

## 1. Attribution — the step that cannot be faked

Two cameras registered over HTTP as `op-multi`, one in each fab; both streams
provisioned; then `UPDATE streams SET fab = NULL` on both, recreating the
pre-feature state.

The pass then ran against a **real** `client_credentials` token for the
`stream-distribution-attribution` service account (ADR-0116) and a **real**
fab-scoped `GET /cameras`:

```
fabsByCamera[munich camera]  = "munich"
fabsByCamera[dresden camera] = "dresden"
attributed                   = 2
stored[munich camera]        = "munich"
stored[dresden camera]       = "dresden"
```

**munich *and* dresden, not everything in munich.** This is the whole
difference from specs 013–015, and the assertion that would fail if the
derivation had quietly defaulted. A single-fab assertion would have passed
either way, which is why both fabs are in one case.

FR-010 separately: a camera the catalogue does not return resolves to nothing,
`Attribute` returns 0, and the stream keeps its null fab. Nothing is defaulted.

### Caveat — what was *not* done this way

The quickstart says to **restart `stream-distribution` and read the log line**.
That is not what happened: the pass was invoked directly against the real
lookup and the real database rather than by restarting the resource, because
the Aspire fixture does not expose a per-resource restart.

So the log line

```
Attributed N stream(s) to a fab; M could not be resolved.
```

and the "silent when there is nothing to attribute" behaviour are covered by
unit tests over the same code path, **not** observed from a real restart. The
resolution, the token, the cross-fab listing and the database write all were.

A restart-based walk is the one thing left to do by hand before merge if a
reviewer wants it.

## 2. The window is real, and fails closed

With `fab = NULL` and no restart, the stream is returned to **nobody**:

- `admin` (munich): `GET /streams/{camera}` → 404, absent from the batch listing.
- `op-multi` (munich + dresden — every fab there is): 404, absent from the listing.

And the control: the *same* stream with its fab intact is 200 with
`"fab": "munich"`. Without that half, the test above would pass against a
listing that was simply broken.

## 3. The scoped reads

| As | Request | Observed |
|---|---|---|
| `op-dresden` | `GET /streams?cameraIdentifiers=<both>` | only the dresden row |
| `op-multi` | same | both rows, `fab` munich + dresden |
| `op-dresden` | `GET /streams/{munich camera}` | **404** |
| `op-dresden` | `GET /streams/{never registered}` | **404** |
| `op-dresden` | `GET /streams?fabId=munich` | **403** |
| `op-dresden` | `GET /streams/{x}?fabId=munich` | **403** |

The two 404 bodies were compared **field by field**, not by status. They are
byte-identical once `traceId` and the camera identifier the caller itself
supplied are normalised — the only two things that can differ between two
distinct requests. A difference in `title` or `type` would let an operator
confirm another plant's camera is streaming.

## 4. The callback is untouched

`POST /streams/authorize` was deliberately not scoped — MediaMTX is not an
operator and holds no fab. Its five integration cases and the WHEP handshake
latency test pass unchanged, and no code path in this feature touches that
route.

## 5. SC-005 — no measurable regression

Read-path latency, 100 samples after 10 warmup, 10 provisioned streams,
measured **before** the scoping landed and again after (T026):

| Route | before | after |
|---|---|---|
| `GET /streams?cameraIdentifiers=<10>` | 9.9 ms median / 28.8 ms p95 | 9.3 / 25.5 |
| `GET /streams/{cameraIdentifier}` | 8.4 ms median / 56.8 ms p95 | 9.4 / 59.8 |

The batch read came out slightly faster and the single read ~1 ms slower at the
median. Both deltas sit inside the run-to-run spread, which for the single
read's p95 is tens of milliseconds. The filter adds one term on an indexed
column, and that is what the numbers show.

The harness was temporary by design and is not in the tree: measured only
afterwards it would have compared the new code against itself, and kept
permanently it would be a latency gate nobody asked for.

## Coverage (T027)

`StreamDistribution.Domain` **94.2%** (gate ≥ 90%), `Application` **89.4%**
(gate ≥ 80%). All twenty gates pass.
