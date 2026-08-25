# Tasks: Two latency legs stop being exempt, and start being watched

**Feature**: `040-kiosk-latency-legs` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1714 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**28 tasks across five phases.** The largest feature in a while, and the only one
whose verification cannot be finished by a machine.

**Phase 1 is load-bearing, not preamble.** §VII's obligation is *conditional* on
§IV's table. Until the table says these legs are built, they carry no obligation
and there is nothing for Phases 2–4 to discharge. It is sequenced first because
it is what creates the work, not because documents come before code.

**Phase 5 is required.** `camera-sim`, `scenario-simulator` and the ICE
host-publishing all sit inside `if (isRunMode && !isE2ETests)`, so **a Playwright
kiosk gets no video**. The automated suite proves the guards and the plumbing and
proves **neither number**. Both figures are obtainable only by a person following
[quickstart.md](./quickstart.md) against the run-mode stack.

**One leg lands partly discharged, and that is the honest outcome** — not a
shortfall to hide. The decode budget spans *SFU sends → kiosk decoded*, and the
browser cannot see the sending end without a clock shared with the SFU.
Establishing one **is** the unbuilt PTP leg.

---

## Do not

- **Do not name the decode figure after its leg.** `receive_to_decoded`, never
  `decode_leg`, and **no budget attached**. It is the cheaper half of a 120 ms
  budget; reported against that budget it would look like the budget passing.
- **Do not record `jitterBufferDelay`.** It measures how long frames wait to be
  played out — that is the **presentation buffer**, a *different* leg, and the
  unbuilt one. Recording it here attributes one leg's time to another.
- **Do not record `totalDecodeTime` alone.** Codec work is single-digit
  milliseconds; against 120 ms it would look magnificent and mean nothing.
- **Do not delete spec 024's wrong finding.** Correct it in place with a dated
  note. Deleting it removes the only trace of how the claim reached three other
  documents.
- **Do not edit issue 1714's body.** Correct it by comment, for the same reason.
- **Do not remove §IV's warning sentence** — *"a leg left recorded as unbuilt
  after it is built would exempt itself from §VII by clerical error"*. It was
  right. It should now be able to point at an instance.
- **Do not change `WhepClient`, `useWhepSession`, or anything the kiosk does.**
  This observes (**FR-011**).
- **Do not add an OpenTelemetry JS SDK.** The browser posts a number; it does not
  export telemetry.
- **Do not use `Date.now()`.** `CellPage` already carries the reason: fab clocks
  are PTP-stepped.

---

## Phase 1: The correction

**Goal**: Four documents that agree with the code, and with each other.

Every correction states what is true **and why the error happened** (**FR-004**) —
a search scoped to `apps/kiosk-web` when the capability lives in `apps/shared`.
The mechanism generalises; the correction does not.

- [ ] T001 [US1] Correct the leg table in `.specify/memory/constitution.md` §IV per [contracts/the-corrected-record.md](./contracts/the-corrected-record.md): `SFU → kiosk decode` becomes **implemented: yes, measured: in part**; `Overlay composite + render` becomes **implemented: yes, measured: yes**. **The load-bearing edit** — the other three documents describe; this one is what §VII is conditional on
- [ ] T002 [US1] Add the prose beneath that table in `.specify/memory/constitution.md` §IV defining **"in part"** the way "recorded, not yet readable" is already defined, and recording the correction's cause. **Keep the warning sentence.** It was correct, and it can now cite an instance
- [ ] T003 [P] [US1] Correct the latency section of `CLAUDE.md`: **one** unbuilt leg (PTP), not three. Keep the instruction to keep §IV current and keep the constitution as the authority — this file summarises and must not compete with it
- [ ] T004 [P] [US1] Add a **dated correction in place** to `specs/024-latency-budget-visible/verification.md` §6. Do not rewrite the finding: it is a record of what was believed then, and deleting it removes the only trace of how the claim propagated. State what is true, what the search missed, and that three other documents took it from here
- [ ] T005 [P] [US1] Correct issue 1714 **by comment**, not by editing the body — same reason as T004. State the true count, the two legs' real state, that PTP remains and stays filed, and what this spec does about the obligation the correction creates
- [ ] T006 [US1] Write `docs/adr/0122-browser-measurements-enter-through-a-service.md`. ADR-0118 decided one sink per environment and never contemplated an emitter that is not a service, because until now there was not one. It decides that a browser measurement reaches observability by being **reported to a service that records it**, preserving the single sink rather than working around it — and records the §4 refusal: a leg may be recorded **in part** under a name that says so, rather than approximated under a name that claims the whole budget

**Checkpoint**: two legs are now subject to §VII, and nothing discharges them yet.

---

## Phase 2: The recording side

**Goal**: Somewhere trustworthy for a number to land.

- [ ] T007 [US2] Add the two kiosk legs to `src/Shared.CQRS/ILatencyBudget.cs`, alongside `RecordEventToOverlayState`. Two methods, not one — **FR-007**: a single combined figure satisfies any assertion that a number exists while measuring neither budget. Document the decode one as a **fragment** in its own XML comment, so a reader of the interface cannot mistake it for the leg
- [ ] T008 [US2] Implement both in `src/ServiceDefaults/LatencyBudget.cs` over the meter it already owns. Instrument names from [contracts/the-two-measurements.md](./contracts/the-two-measurements.md): `kiosk.overlay_draw` and `kiosk.receive_to_decoded`. **No budget is attached to the second**
- [ ] T009 [US3] Enforce both guards in `src/ServiceDefaults/LatencyBudget.cs`, in the implementation and not at call sites — the reason is already written in `ILatencyBudget`'s doc comment and applies unchanged: a leg with no recorded start must record **nothing, never a zero**, and a negative elapsed time is a stepped clock rather than a fast journey. Add the third case a browser introduces: an elapsed time large enough to describe a **suspended page** rather than a journey
- [ ] T010 [US2] Add the receiving endpoint to `src/StreamDistribution/Api/StreamEndpoints.cs` — the context the kiosk already calls about what it is displaying (`/authorize` for WHEP). It accepts `{ measurement, camera, elapsedMilliseconds }` and records through `ILatencyBudget`. **A browser-reported number is untrusted input** (§VIII): validate at the boundary, and let the guards in T009 be the enforcement point rather than trusting the sender

**Checkpoint**: a number posted by anything lands correctly, or is correctly refused.

---

## Phase 3: The measuring side

**Goal**: Two figures, from a kiosk that behaves exactly as it did.

- [ ] T011 [US2] Create `apps/shared/src/observability/kioskLatency.ts`: compute both elapsed times and post them. **Post the number, never the start** — a slow post then makes the report late, never the measurement large. Follow `resilienceLog.ts`'s shape; it is the only browser-observability idiom here and a second one in the same folder would be worse than none
- [ ] T012 [US2] Measure **overlay draw** in `apps/kiosk-web/src/features/cell/CellPage.tsx`: `performance.now()` when the overlay's state changes, then two chained animation frames to reach after-paint. The first runs after React commits and before paint; the second after that paint. **`performance.now()`, never `Date.now()`** — the file already carries the reason
- [ ] T013 [US2] Measure **receive-to-decoded** in `apps/shared/src/ui/composites/CameraViewer.tsx` from `RTCPeerConnection.getStats()`, `inbound-rtp`: `(totalProcessingDelay + totalDecodeTime) / framesDecoded`, sampled as a delta between reads. **Observe only** — do not touch `WhepClient` or `useWhepSession`, and do not alter the connection's behaviour (**FR-011**)
- [ ] T014 [US2] Carry the **tile's camera** as a dimension on both figures, in `apps/shared/src/observability/kioskLatency.ts`. Per-tile, not per-wall: a wall average hides one frozen camera among three good ones, which is exactly the failure an operator reports and an average does not show
- [ ] T015 [US3] Apply both guards **browser-side too**, in `apps/shared/src/observability/kioskLatency.ts` — a figure that fails one should not be sent at all. This does not replace T009: the browser is untrusted and the service is where the guards are *enforced*
- [ ] T016 [US4] Emit a structured `console.info` line per measurement from `apps/shared/src/observability/kioskLatency.ts`, matching `resilienceLog.ts`'s `[resilience]` prefix contract. **Alongside the post, not instead** — a console line is exactly the *recorded, not readable* state the constitution calls half discharged. It is here because it costs nothing and it is what makes Phase 5 practical
- [ ] T017 [US2] Verify **FR-012**: the observer is not a meaningful share of a 50 ms budget. Two callbacks and a subtraction on a path that already re-renders. This task is *check and record the reasoning*, not *optimise*

**Checkpoint**: both numbers exist in a running kiosk.

---

## Phase 4: Evidence — the half a machine can give

**This phase proves the guards and the plumbing. It proves neither number.**
Saying so is not modesty: a green suite asserting a leg it never exercised is the
same class of claim as a document saying a leg is unbuilt when it runs on every
kiosk, which is the thing this feature exists to fix.

- [ ] T018 [P] [US1] Assert `.specify/memory/constitution.md` §IV says **implemented** for both legs, in a new `tests/Architecture.Tests/LatencyLegRecordTests.cs`. **The assertion is on the document**, because the failure was a document saying something false and nothing noticing
- [ ] T019 [US1] Assert §IV distinguishes **four** states across six legs — watched, in part, recorded-not-readable, unbuilt — in `tests/Architecture.Tests/LatencyLegRecordTests.cs`. **SC-007.** Rounding any one up repeats the failure, and a test that only checks "no leg says unbuilt" would pass against a table that rounded three of them up
- [ ] T020 [P] [US3] Assert the guards in `tests/ServiceDefaults.Tests/LatencyBudgetTests.cs`: an unknown start records **nothing** — asserted as an **absence**, never as a recording of zero; a negative elapsed time records nothing; a suspended-page-sized elapsed time records nothing
- [ ] T021 [P] [US2] Assert the two figures are **separable** in `tests/ServiceDefaults.Tests/LatencyBudgetTests.cs`: recording one does not move the other. One combined number satisfies any assertion that a number exists while measuring neither budget
- [ ] T022 [US2] Assert the decode instrument's **name** does not claim the leg and that **no budget is attached** to it, in `tests/ServiceDefaults.Tests/LatencyBudgetTests.cs`. This is the assertion that stops a fragment being reported as a leg passing — the single most likely way this feature ships something false
- [ ] T023 [P] [US3] Assert the endpoint validates and refuses in `tests/Integration.Tests/StreamDistribution/KioskLatencyIntegrationTests.cs`: a well-formed report records; a malformed or out-of-range one is refused rather than recorded
- [ ] T024 [P] [US2] Assert the browser side in `apps/shared/src/observability/kioskLatency.test.ts`: both figures computed separately, both guards applied before sending, the camera carried, the console line emitted. **No real stream involved** — these are the guards, not the numbers
- [ ] T025 [US4] Assert the kiosk **behaves exactly as before** (**FR-011**): `apps/kiosk-web` and `apps/shared` suites pass, and the `CellPage`, `CameraViewer` and `WhepClient` tests are **untouched**. Show the untouched ones as an empty `git diff` — if any needed editing, the observer changed behaviour and that is a finding

**Checkpoint**: everything a machine can check, checked — and labelled as that.

---

## Phase 5: Evidence — the half only a person can give

**Required.** Not a suggestion, not a nice-to-have.

- [ ] T026 Follow [quickstart.md](./quickstart.md) against the **run-mode** stack (`dotnet run --project src/AppHost`, not the e2e profile): publish a **two-tile** layout, open the kiosk, confirm both tiles show moving video with overlays drawn on them, and **read both numbers from the Aspire dashboard**. Record the values, per tile. Not "the metric is emitted" — the number, and where it was read
- [ ] T027 Provoke the guards by hand, per [quickstart.md](./quickstart.md): stop a camera's clip and confirm **no** figure is recorded for the gap; background the tab for ten seconds and confirm no figure spans it; reconnect and confirm the recovery is timed as a new journey. Record what was done and that nothing was recorded
- [ ] T028 Write the verification note on the PR. It must state **which claims rest on Phase 5 and cannot be checked by CI**, name the four corrected documents, name the four leg states in §IV, give both figures with the values read, and confirm the decode figure carries no budget. **Say plainly that CI cannot produce video and that the automated suite proves the guards and the plumbing only** — the alternative is a green tick standing in for something nobody saw

---

## Dependencies

```
T001 ─▶ T002 ─┐
T003, T004,   │   (the four corrections — parallel with each other)
T005          │
T006 (ADR)    │
              ▼
     T007 ─▶ T008 ─▶ T009      (the contract, the meter, the guards)
                │
                ▼
              T010             (the endpoint)
                │
                ▼
     T011 ─▶ T012, T013, T014, T015, T016, T017
                │
                ▼
     T018–T025  (automated evidence)
                │
                ▼
     T026 ─▶ T027 ─▶ T028      (the evidence CI cannot give)
```

**Phase 1 blocks nothing technically.** The code would compile without it. It is
first because it is what makes the rest *obligatory* — and doing the measurement
first and the correction after would mean shipping work nobody could say why was
needed.

**T009 before T010**, because an endpoint that records before the guards exist
records the things the guards are for.

**T026–T028 need everything**, and T028 needs T026 and T027 to have actually
happened rather than to be planned.

---

## Parallel opportunities

- **T003, T004, T005** — three different documents, no shared state. T001/T002
  are sequential with each other (same file, and T002 depends on T001's table).
- **T006** (the ADR) is independent of all four corrections.
- **T012 and T013** — different files, different legs, genuinely concurrent once
  T011 exists.
- **T018, T020, T021, T023, T024** — different test files.
- **T017 and T025** are both *check and record*, need no new code, and can run
  once their subject lands.

---

## Implementation strategy

**MVP is T010.** With the record corrected and a number landing correctly, the
obligation exists and the mechanism works. Phase 3 supplies real figures; Phases
4–5 prove it.

**Do Phase 1 first and completely.** Four documents currently agree with each
other and disagree with the code. Correcting one and leaving three is the same
defect with a smaller blast radius, and it is the easy thing to do accidentally.

**Do T022 before you believe the decode figure.** The name is the only thing
standing between an honest fragment and a false claim that a 120 ms budget is
being met.

**Budget real time for Phase 5.** It needs the stack up, a seeded catalog, a
published two-tile layout and a browser. It is the only place either number can
be seen, and it is not something to do hurriedly at the end.

---

## Three things most likely to go wrong

1. **The decode fragment gets reported as the leg.** It is the cheaper half of a
   120 ms budget, so it will look excellent, and everything about the surrounding
   machinery invites attaching the budget to it. That would convert a known gap
   into a false claim that the gap is closed — strictly worse than the state this
   feature started from. T022 asserts the name and the absent budget; the contract
   states it; §IV records the leg as measured **in part**.

2. **Phase 5 gets skipped because Phase 4 is green.** Twenty-five automated tasks
   pass and it feels finished. They prove the guards and the plumbing; neither
   number has been seen. A green suite standing in for an unexercised claim is
   precisely the failure mode that produced this issue. T028 requires the PR to
   say which claims rest on a person.

3. **The correction lands in one document.** Four repeat the claim, and each reads
   plausibly on its own. Mitigated by the contract holding all four together and
   by T018/T019 asserting the constitution's own text — but nothing asserts
   `CLAUDE.md` or spec 024's note, so those two rest on the contract and on
   review.
