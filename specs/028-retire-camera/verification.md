# Verification: Retire a camera

**Feature**: `028-retire-camera` · observed **2026-08-27** against the run-mode stack

**Status: every checklist item holds.** The two the quickstart singles out as most
likely to be wrong — one audit row for a double retirement, and no further health
announcements — both hold, and the second was checked **with a control** rather
than by observing an absence.

**One defect found in passing**, unrelated to retirement but discovered by trying
to run this procedure: `StreamHealthChangedV1` is published with **no fab**, so a
fab-scoped audit query never returns any stream-health row. See §5.

**Why this is a file.** T035 says "verification note on the PR". Spec 028 landed
across three merged PRs (#1846, #1847, #1848), so there is no single PR to put it
on, and a note on a merged PR is not where anyone looks. It sits here instead,
beside spec 024's and spec 040's, and the PRs carry a pointer.

---

## 1. The checklist

| Check | FR | Result |
|---|---|---|
| Retire returns 204; second retire also 204; **one** audit row | FR-005 | **204, 204, one `CameraRetiredV1`** |
| Another fab's camera returns 404, not 403 | FR-004 | Covered by test — see §6 |
| Name reusable in same fab after retiring; still 409 before | FR-006 | **409 before, 201 after** |
| Name reuse does not leak across fabs | FR-006 | Covered by test — see §6 |
| Retired camera absent from `GET /cameras`; present with `includeRetired=true` | FR-007 | **Absent; present with `"status":"Decommissioned"`** |
| SFU path removed; stream terminal; row still present | FR-008 | **Path 200 → 404; stream `"state":"Retired"`, row still readable** |
| Retirement succeeds even with the SFU unreachable | FR-008a | Covered by test — see §6 |
| No health announcements for the retired camera afterwards | research §4 | **Holds, with a control** — §4 |

Camera `01a044e8-c871-77e4-a28c-b2c90b1ac08c`, fab `munich`, retired at
**20:28:55Z**.

**FR-005 in detail.** Retired twice, both 204. The audit timeline for that camera
holds exactly two rows — `CameraRegisteredV1` then `CameraRetiredV1` — so the
second retirement announced nothing. That is the defect the quickstart warns
about ("a second `CameraRetiredV1` is the most likely defect here, and returning
204 hides it"), and it is absent.

**FR-008 in detail.** Before retiring, `GET /v3/config/paths/get/cam-<guid>` on
the SFU returned **200** with `source: rtsp://camera-sim:8554/t035-probe`. After,
**404**. The stream row survives: `GET /streams/<guid>` returns 200 with
`"state":"Retired"` — the terminal value — and keeps its `lastSuccessAt`.

---

## 2. The trace across both contexts

Trace `1dec9d2f52bb16c71438423de8776219`, **163 ms**, one trace spanning both
contexts, exactly the shape the quickstart predicts:

```
camera-catalog     Server    POST /cameras/{camera:guid}/retire   204   22 ms
  ├─ Producer  send     CameraRetiredV1                → rabbitmq
  ├─ Consumer  receive  CameraRetiredV1   audit-observability      27 ms
  └─ Consumer  receive  CameraRetiredV1   stream-distribution     130 ms
       ├─ Producer  "observe stream health change"    (spec 027 journey)
       │    ├─ Producer  send     StreamHealthChangedV1
       │    └─ Consumer  receive  StreamHealthChangedV1  audit-observability  57 ms
       └─ Client    DELETE → mediamtx
                    /v3/config/paths/delete/cam-01a044f6-…   200 OK   61 ms
```

Both consumers hang off the single `POST …/retire` Server span, so the
retirement is one journey rather than two unrelated ones. The `observe stream
health change` Producer span is spec 027's named origin, and it is what carries
the `Healthy → Retired` transition — see §3.

**The scenario simulator was stopped for this**, as the quickstart instructs. It
publishes plant-floor events about twice a second and the trace listing returns
only the newest handful, so the retirement trace ages out of reach within seconds
otherwise. It was restarted afterwards.

---

## 3. The one health event after retirement is the retirement

The retired camera has exactly three `StreamHealthChangedV1` events, ever:

| Time | Transition | Error |
|---|---|---|
| 20:28:23 | `Provisioning → Degraded` | `not ready` |
| 20:28:28 | `Degraded → Healthy` | — |
| **20:28:56** | **`Healthy → Retired`** | — |

The last one is timestamped one second **after** the retirement call, which looks
alarming and is not: it is published **inside the retirement trace** (§2), as the
transition into the terminal state. It is the retirement announcing itself, not a
sweep finding a retired camera. Recorded here because the timestamp invites
exactly the wrong reading.

**After that, nothing** — checked ~15 minutes later.

---

## 4. "No further announcements", checked with a control

An absence proves nothing on its own: if the watcher announced nothing for
*anybody* in that window, a retired camera's silence is not evidence. So a
control was run.

A **second, non-retired** camera had its SFU path deleted at **20:37:52** —
breaking it the same way retirement does, without retiring it.

| | Retired camera | Control camera (broken, not retired) |
|---|---|---|
| State after | `Retired` | `Degraded`, error `path not registered` |
| `StreamHealthChangedV1` after 20:37:52 | **0** | **1**, at 20:38:08 |

So the watcher **was** sweeping and **was** announcing changes in that exact
window — it announced for the control within ~16 s — and announced nothing for
the retired camera. That is the claim research §4 makes, and it holds.

The watcher polls every **2 s** (`StreamHealthWatcher.PollInterval`), and
excludes retired streams explicitly:

```csharp
.Where(stream => stream.State != StreamState.Retired)
```

---

## 5. Found while verifying: stream-health events carry no fab

**`StreamHealthChangedV1` is published with `Fab: null`.** Its metadata is built as

```csharp
Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.ChangedAt, null, null)
```

and `EventMetadata`'s third parameter is `Fab`. The `Stream` aggregate *has* a
fab — `GET /streams/<guid>` returns `"fab":"munich"` — but
`StreamHealthChangedDomainEvent` does not carry it, so the integration event
cannot.

Two consequences:

1. **A fab-scoped audit query never returns a stream-health row.**
   `GET /audit?fabId=munich&eventKind=StreamHealthChangedV1` returns **0** rows;
   dropping `fabId` returns them all. An operator scoped to a fab cannot see
   stream health in the audit at all.
2. **This check, run the natural way, cannot fail.** Verifying research §4
   through the fab-scoped audit API returns 0 whether or not the watcher is
   announcing — which is precisely how this verification nearly recorded a pass
   for the wrong reason. The control in §4 is what caught it.

The quickstart's own SQL (`payload->>'Camera' = …`, no fab predicate) is not
affected. Raised separately rather than absorbed here.

---

## 6. What is automated, and what rested on this note

**Automated**, in `RetireCameraIntegrationTests` and
`CameraNonEnumerationIntegrationTests`, running in CI's integration job:

- `Retiring_a_camera_succeeds_and_retiring_it_again_announces_nothing_further`
- `Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is` (FR-004)
- `A_retirement_in_one_fab_changes_nothing_in_another` (FR-006 cross-fab)
- `A_retired_cameras_name_is_free_again_in_its_own_fab`, `An_active_cameras_name_is_still_refused`
- `Case_insensitivity_survives_reuse`
- `A_retired_camera_leaves_the_default_listing_and_comes_back_when_asked_for`

The cross-fab items are stated as covered by those tests rather than repeated by
hand: this stack's realm gives every mintable client and the `operator` user the
**munich** fab only, so a second fab's camera cannot be created here without
standing up another user's credentials. The tests do it properly, with both fabs.

**Not automatable, and therefore resting on this note:** the trace across both
contexts (§2), and that the watcher genuinely goes quiet for a retired camera
while still announcing for a broken one (§4). Neither is visible to a test that
does not watch a live system over time.
