# Verification: Two latency legs stop being exempt, and start being watched

**Feature**: `040-kiosk-latency-legs` · issue 1714 · observed **2026-08-27**

**Status: both figures have been read, per tile, and all three guards have been
provoked.** Phase 5 is done. Stated first rather than at the end, because the
whole point of this feature is that a green tick must not stand in for something
nobody saw.

**Why this is a file and not a PR comment.** T028 says "write the verification
note on the PR". Spec 040's PR — **#1883** — was **closed without merging** while
its work landed on `develop` anyway: the stacked-PR loss mode `CLAUDE.md`
describes, where deleting a parent branch closes its children and a closed PR
cannot be reopened. A verification note living only on a closed PR would be
recorded and not readable, which is the exact failure state §IV has a column for.
So the note lives here, beside spec 024's, and the PR body carries a copy.

---

## 1. What CI cannot check, and therefore what rests on this note

**CI cannot produce video.** `camera-sim`, `scenario-simulator` and the ICE
host-publishing all sit inside `if (isRunMode && !isE2ETests)` in the AppHost, so
a Playwright kiosk in CI gets no media at all. No decoded frame means no decode
figure; no rendered overlay means no overlay-draw figure.

**The automated suite proves the guards and the plumbing, and nothing else.** It
proves that a figure with no start records nothing rather than a zero, that a
negative or absurd elapsed time is refused, that the endpoint validates its
input, that only a kiosk principal may record into these segments, that the two
segments are separable, that the fragment is not named after its leg, and that
the four documents say what they are asserted to say. Every one of those is a
statement about code.

**These claims rest on this note and cannot be checked by CI:**

1. That either number exists in reality at all.
2. Their values, and that they are per tile rather than one blended histogram.
3. That both tiles show moving video with an overlay drawn onto it.
4. That the guards hold against a real stream dropping, rather than against a
   unit test's arranged inputs.
5. That the recovery after a real reconnect is timed as a new journey.

---

## 2. The correction landed in four documents, and they agree

| # | Document | Where |
|---|---|---|
| 1 | `.specify/memory/constitution.md` §IV | The leg table + the "in part" definition beneath it |
| 2 | `CLAUDE.md` | Latency-budget section: **one** unbuilt leg (PTP), not three |
| 3 | `specs/024-latency-budget-visible/verification.md` | §6, dated correction in place — *"⚠ Correction, 2026-08-25 (spec 040)"* |
| 4 | Issue 1714 | Comment of 2026-08-25, *"Correction: one leg is unbuilt, not three (spec 040)"* — the body left unedited, deliberately |

All four were re-read on 2026-08-27 and agree: the presentation buffer (PTP) is
the single unbuilt leg. Spec 024's wrong finding is corrected **in place** rather
than deleted, and 1714's body is corrected **by comment**, so in both cases the
trace of how the claim propagated survives.

---

## 3. §IV records four states across six legs

**SC-007 exists to stop any of these being rounded up**, so each is named:

| Leg | State |
|---|---|
| Camera → SFU | **measured** — SFU metrics |
| SFU → kiosk decode | **in part** — `receive-to-decoded` only |
| Presentation buffer (PTP) | **unbuilt** — not yet subject to §VII; issue 1714 |
| Event → overlay state | **recorded, not yet readable** — the number exists, nothing outside the process can read it (spec 025) |
| Overlay composite + render | **measured** |
| Headroom | not a leg — an arithmetic remainder |

"In part" and "recorded, not yet readable" are distinct on purpose, and neither
is "measured". An unbuilt leg is **not yet subject** to §VII rather than exempt
from it, and the obligation attaches to whichever spec builds it (ADR-0117).

---

## 4. Both figures, read from the dashboard, per tile (T026)

Read from the Aspire dashboard's Metrics page: resource **`stream-distribution`**,
meter **`SmartSentinelEye.Latency`**, instrument
**`sse.latency.segment.duration`**, Table view, window Last 5 minutes. A
published **two-tile** wall; both tiles showing moving video with overlays drawn
(`MOULDING — CYCLE TIME`, `SMD — PLACEMENT RATE`), motion confirmed by comparing
two grabs of each tile five seconds apart rather than by a single screenshot.

Per one-minute bucket, in milliseconds, filtered to one segment and one camera at
a time. The filter was re-read *after* each reading and the reading kept only if
it still held — 4 of 4 held, all four distinct.

| Tile | Segment | P50 | P90 | P99 | Buckets |
|---|---|---|---|---|---|
| `…0e0e` Injection Moulding | `kiosk-receive-to-decoded` | 25–100 | 25–100 | 25–100 | 10 |
| `…0e0e` | `kiosk-overlay-draw` | 250 | 250 | 250 | 1 |
| `…1215` SMD Placement Line | `kiosk-receive-to-decoded` | 25–75 | 25–100 | 25–100 | 5 |
| `…1215` | `kiosk-overlay-draw` | 250 | 250 | 250 | 1 |

**The dashboard's numbers are bucket boundaries, not measurements.** Its
histogram buckets are coarse (25/50/75/100/250/500…), so an overlay-draw of 250
means *"in the bucket ending at 250"*. The browser's own values for those same
two samples were **139.4 ms** and **100.5 ms**. Cited so the 250 is not later
read as a measured quarter-second.

**Overlay draw is over its 50 ms budget**, and per **ADR-0123** that is read as
cadence before compositing: this leg includes the wait for the frame that carries
the change, so it has a floor of roughly 1.5–2 frame intervals. 100–139 ms
implies **~15–20 Hz**, under the ≥ 30 Hz ADR-0123 states the budget requires.
That is a statement about a developer machine decoding two streams under
Playwright, not about the compositing code.

**One overlay-draw sample per tile is not a distribution.** `overlay_draw` fires
on overlay *change*, and the scenario overlays carry static labels, so each tile
draws once and has nothing to redraw. A distribution needs an overlay whose text
actually changes.

---

## 5. The decode figure carries no budget of its own

**Confirmed, and worth stating precisely, because the tag looks like the
opposite.** The measurement is named `kiosk-receive-to-decoded` — after what it
measures, never `decode_leg`. It is constructed as:

```csharp
new("kiosk-receive-to-decoded", "sfu-to-kiosk-decode", 120, isWholeLeg: false);
```

The `120` is **the enclosing leg's** budget, not this figure's threshold —
`LegBudgetMilliseconds` is documented as *"The whole leg's budget, even when this
segment is part of it"* — and it travels with `IsWholeLeg = false`, whose own
comment is the instruction: *"A dashboard comparing a fragment to the leg's
budget is comparing the wrong things, and it should be able to say so."* Nothing
scores this figure against 120. Compare `kiosk-overlay-draw`, which is
`isWholeLeg: true` at 50 ms and **is** its leg.

**An observation from actually reading the dashboard, rather than the code.** On
the Metrics page the decode figures appear alongside a `leg.budget_ms = 120` tag,
and the only thing preventing a reader from treating that as a passing threshold
is noticing `segment.is_whole_leg = false` in a neighbouring row. The protection
exists and is exactly one boolean. `tasks.md` names "the decode fragment gets
reported as the leg" as the single most likely thing to go wrong, so this is
recorded as a live risk rather than a closed one.

---

## 6. The guards were provoked, not merely unit-tested (T027)

Same two-tile wall, 62 figures over 3m 24s. The clip was stopped **at the SFU**,
by pointing one camera's MediaMTX path at a dead address and restoring it.

**One tile was stopped and the other left running as a control.** Without a
control, a gap in the figures is equally well explained by the whole stack
stalling, and the guard gets credited for someone else's silence.

| | Stopped tile | Control tile |
|---|---|---|
| Figures across the 45 s gap | **0** | **9** |

- **FR-008 — no figure for the gap, not a zero: holds.** Zeros recorded across
  the whole run: **0**.
- **FR-009 — no figure spans a backgrounded tab: holds.** Hidden 10.1 s; no
  figure ≥ 9 s; largest figure in the entire run **58.9 ms**. *Stated with its
  limit:* Chromium does not meaningfully throttle a 5 s interval over a 10 s
  hide, so the sampler kept cadence and there was no long delta for the guard to
  reject. Ten seconds is what the task asks for; stressing the 60 s ceiling would
  need a hide of minutes.
- **Reconnect — timed as a new journey: holds.** Restored at 158.1 s, first
  figure at **173.8 s (+15.7 s)** — matching the jittered retry ladder capped at
  15 s in `useWhepSession`, which is the evidence the session was torn down and
  re-established rather than merely starved. The figure was **16.4 ms, not 60
  seconds**. Two mechanisms in the code guarantee that: `CameraViewer`'s sampler
  effect is keyed on `status === 'live'` and holds `previous` *inside* the
  effect, so a drop discards the baseline; and `decodeElapsedBetween` returns
  `null` when the counters go backwards, which is what a restarted session's
  counters do.

**Two approaches that do not work**, recorded so nobody repeats them:
`context.setOffline(true)` does **not** stop WebRTC media — figures kept arriving
through the whole "outage" and the tiles never left `live`, while a naive *"did
figures resume?"* check passed anyway because they had never stopped. And
restarting `camera-sim` did not return the stream to an already-open kiosk within
88 s, which is what blocked this task on the first attempt.

---

## 7. The kiosk behaves as before (FR-011)

Asserted automatically by T025, and seen during both manual runs: the same
picture, the same overlay drawn onto the live frame by the shared `CameraViewer`
composite, and the same reconnection behaviour — the retry ladder recovered the
stopped tile on its own, unchanged. Nothing in `WhepClient` or `useWhepSession`
was modified; the decode statistics are read from a connection those already own.

---

## 8. Two things this verification turned up, and did not settle

**§IV's Dashboard column reads `no` for both kiosk legs, and whether that is
correct is genuinely undecided — two ADRs a day apart say different things.**

> **⚠ This section has been wrong twice, and both corrections are kept.**
> It first asked whether the column was stale and said the question needed a
> decision. It was then "corrected" to say ADR-0117 had already settled it —
> asserted **without reading ADR-0118**, which is the ADR that speaks directly to
> what satisfies §VII. Both versions stay visible: this is the exact failure this
> feature exists to correct, committed twice on the same question, and deleting
> the evidence would make the third version look like insight.

**ADR-0117** (2026-08-21), amending §VII's dashboard bullet:

> A leg whose code path exists MUST have a latency measurement **and a dashboard
> showing it against its ADR-015 budget** before further work ships on that leg.

**ADR-0118** (2026-08-22, one day later), amending §VII's *third* bullet:

> **Development and CI: the Aspire dashboard.** … It is sufficient for a human
> answering "what happened", and **it is what §VII's dashboard requirement is
> satisfied by in development**.

Those cannot both be applied literally. The Aspire metrics explorer displays a
raw histogram and cannot show a value against a threshold, so under ADR-0117
nothing in development discharges the obligation; under ADR-0118 the sink itself
does. ADR-0118 is the later document and names ADR-0117 only under **Relates
to** — it does not supersede it, and it amends a different bullet.

ADR-0118's own clause 4 leans back the other way: *"§VII's 'measured' must
eventually mean 'someone can consult it', and today it does not. This ADR records
that gap rather than closing it."*

**The honest state.** The two kiosk legs are **measured**, and their figures
**are** consultable in the development sink — T026 read them, per tile, with
values. Whether that discharges §VII, or whether "against its ADR-015 budget"
still binds and nothing in development can satisfy it, is a constitutional
reading that no document settles.

Spec 024 reached the same impasse and handled it the right way:

> The choice is a constitutional reading rather than an implementation decision.
> … **No exception is requested. The situation is reported for judgement.**

Reported for judgement here too, and tracked as issue 1940. **A verification note
is not the place to decide it** — which is what this section said in the first
place, before I talked myself out of it.

**The decode fragment sits next to a budget it must not be compared against** —
see §5. Recorded as a live risk.

---

## 9. What is automated, and what is not

CI proves the guards and the plumbing. **CI cannot produce video, and therefore
cannot produce either number.** Everything in §4 and §6 above was observed by a
person driving a run-mode stack, and rests on this note. A green suite that never
saw a frame is the same class of claim as a document saying a leg is unbuilt when
it runs on every kiosk — which is the error this feature was written to correct.
