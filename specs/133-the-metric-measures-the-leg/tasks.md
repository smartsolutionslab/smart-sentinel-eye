# Tasks — spec 133

Tracked at feature granularity against issue **#2173** (CLAUDE.md, Phase 3).
No per-task issues — that stopped after spec 028.

## Phase 4a — tests first, observed RED

- [ ] **T001** Give `tests/LayoutComposition.Application.Tests/Fakes/FakeClock.cs` an
  `Advance(TimeSpan)` so a test can put time *inside* an awaited call. Existing
  construct-and-read call sites are unaffected.
- [ ] **T002** Give `tests/LayoutComposition.Application.Tests/Fakes/RecordingLatencyBudget.cs`
  an optional clock and record **when** the handler reported, alongside what it
  reported. The fake must not re-implement the production subtraction — the test
  computes the span from `observedAt - rootIngestedAt`.
- [ ] **T003** `ResolvedOverlayTextChangedV1HandlerTests`: the recorded span
  includes time spent in the push (broadcaster advances the clock 750 ms).
  **RED — nothing is recorded today.**
- [ ] **T004** `ResolvedOverlayTextChangedV1HandlerTests`: a frame with no root
  hands the absent moment to the budget rather than a zero; a frame with no fab
  is not broadcast and records nothing.
- [ ] **T005** `SystemVariableValueRequestedV1HandlerTests`: the value write
  reports no measurement — the leg is not terminated there. **RED — it reports one today.**
- [ ] **T006** `SystemVariableValueRequestedV1HandlerTests` (or the command/domain
  handler tests): the acceptance moment survives from the requested message to
  the published `ResolvedOverlayTextChangedV1`. **RED — null today.**
- [ ] **T007** Run all four affected projects; capture the failures verbatim into
  `verification.md`.

## Phase 4b — implementation, RED → GREEN

- [ ] **T008** `SetVariableValueCommand` gains `Option<DateTimeOffset> RootIngestedAt`.
- [ ] **T009** `Variable.SetValue` takes it and stamps it on
  `VariableValueChangedDomainEvent`; the domain event gains the component.
- [ ] **T010** `SetVariableValueCommandHandler` deconstructs and forwards it.
- [ ] **T011** `VariableValueChangedDomainEventHandler` fills `EventMetadata`'s
  fifth argument on `ResolvedOverlayTextChangedV1`.
- [ ] **T012** `SystemVariableValueRequestedV1Handler` passes the root on the
  command and **drops** `ILatencyBudget`.
- [ ] **T013** `ResolvedOverlayTextChangedV1Handler` takes `ILatencyBudget` and
  records **after** the broadcast, matching `OverlayHighlightRequestedV1Handler.cs:49`.
- [ ] **T014** Register nothing new — `ILatencyBudget` is already a singleton in
  `WolverineDefaults`; confirm LayoutComposition's handler resolves.
- [ ] **T015** Update the two XML doc comments that define what this number means
  (`ILatencyBudget`, `LatencySegment.EventToOverlayState`) with the step change.
- [ ] **T016** Counterfactual: temporarily move the recording above the broadcast
  and confirm T003 fails. Restore.

## Phase 5 — verification

- [ ] **T017** `dotnet build -c Release` clean (AppHost stopped first — MSB3027).
- [ ] **T018** Four test projects green; counts recorded.
- [ ] **T019** `verification.md` — red verbatim, green, counterfactual, what the
  instrument measures now, and that no leg's actual cost changed.

## Out of scope, recorded so it is not silently skipped

- §IV's *Measured* cell and its stale prose — **reported, not edited** (#2072, #2012).
- Whether 758 ms is an ADR-class breach — #2072.
- Whether the Dashboard column needs a purpose-built view — #1940.
