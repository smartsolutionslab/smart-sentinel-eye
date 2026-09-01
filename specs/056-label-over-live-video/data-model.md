# Data model — 056 label over live video

Phase 1. **No persisted entity is added.** Nothing here reaches a database, a
message contract, or a domain model. What follows are the values the fixture
and the measurement pass around, written down because two of them are easy to
get subtly wrong and one of them is the whole deliverable.

---

## 1. `LiveVideoWall` — the names one seed creates and one teardown removes

Mirrors the existing `bound-overlay-wall.ts`: one module owns the names so a
test never spells one itself and a teardown never guesses.

| Field | Meaning |
|---|---|
| `cameraName` | carries the end-to-end prefix, which is what the teardown matches on |
| `cameraRtspUrl` | the address of the fixture's video source, **from configuration** |
| `variableName` | the system variable the overlay binds to |
| `overlayName` | the overlay whose text resolves from that variable |
| `layoutName` | the published wall holding a single tile |

**Rule**: every name carries the end-to-end prefix. The teardown matches the
prefix, so a name that omits it survives the run — which is how a fixture
leaves rows behind, a defect a prior spec fixed and which must not regress.

**Rule**: `cameraRtspUrl` is **supplied, never composed in a test**. A host and
port written into a spec file is a second thing to keep true, and it rots
silently — the wall it produces renders `404` and looks like a broken product
rather than a broken fixture.

---

## 2. `DecodeEvidence` — what makes a picture *moving* rather than *present*

| Field | Meaning |
|---|---|
| `first` | a `DecodeSample` (existing type) |
| `second` | a `DecodeSample` taken **1000 ms** later |
| `framesAdvanced` | `second.framesDecoded - first.framesDecoded` |
| `isOngoing` | `framesAdvanced >= 10` |

**Why a pair and not a reading.** A single non-zero `framesDecoded` is
satisfied by a source that emitted one frame and stopped — a frozen wall, which
is indistinguishable from a working one in a screenshot and is exactly the
failure this feature exists to catch. Only the delta says the picture moves.

**Reused, not rebuilt.** `DecodeSample` and its reader `decodeSampleFrom`
already exist in `kioskLatency.ts`. A second reader of the same statistics is a
second thing to get wrong.

---

## 3. `SpanMeasurement` — one duration, or an honest refusal

| Field | Meaning |
|---|---|
| `submittedAt` | stamped by the **test process** when the value is submitted |
| `observedAt` | stamped by the **test process** when the new text is visible |
| `elapsedMilliseconds` | `observedAt - submittedAt`, one subtraction on one clock |
| `refusal` | set when the span could **not** be measured; excludes a figure |

**Exactly one of `elapsedMilliseconds` and `refusal` is present.** A type that
allows both, or neither, permits the outcome this feature exists to prevent: a
number reported beside the reason it should not have been.

**Both stamps come from one process on one machine.** That is the shape spec
053 found safe (two readers, one OS clock), as against the shape it found *not
established* (a host stamp minus a container stamp). The browser's clock is
never mixed in.

---

## 4. `SpanReport` — a figure is not interpretable without its conditions

| Field | Meaning |
|---|---|
| `iterations` | **every** measurement, not a summary |
| `medianMilliseconds` | the middle figure |
| `rangeMilliseconds` | lowest and highest |
| `legsCovered` | event → overlay state; overlay composite + render |
| `legsNotCovered` | camera → SFU; SFU → decode; presentation buffer |
| `conditions` | where it ran, what else was running, what the wall held |
| `includesLabelHold` | **true** — the aging is inside the span, by design |

**`legsNotCovered` is a field rather than a footnote.** A figure under 800 ms
does not establish the budget is met, because three legs are absent from it. A
reader who sees the number without that list draws the wrong conclusion, and the
structure is what stops the two travelling apart.

**`iterations` carries the raw figures.** A median without its spread hides
whether the machine or the system under test is the bottleneck — which is the
distinction that made an earlier "~3x" claim wrong.

**`includesLabelHold` is recorded** because it is the difference between this
figure and any figure taken before it. Every prior end-to-end run had a null
frame age, so the hold never engaged; a figure from a video-less wall describes
a different system.

---

## What is deliberately absent

- **No new latency measurement name.** The kiosk's reported set stays the five
  it validates today. This span is reported by the test, not by the kiosk, so
  no contract widens.
- **No new persisted row.** The fixture creates a camera, a variable, an
  overlay and a layout through existing paths, and removes them.
- **No summed figure, anywhere.** There is no field for one, which is the
  cheapest way to ensure nobody fills it in.
