# Verification: The event-to-overlay leg can be measured

**Feature**: `025-measure-event-to-overlay` · observed 2026-08-22

**In progress.** T001–T003 are done and they settled the route. This note records
that first, because it is the part that decided what the feature is.

---

## 1. The question (T001) — trace parentage is dropped

Phase 0 suspected it from spec 024's captures. Confirmed on a running stack.

**Every publish from `event-ingestion` is its own root trace**, containing only
`event-ingestion` spans — 23 of 23 sampled, none containing a downstream
`receive`:

```
trace 6a3c5d51…   send FabEventIngestedV1   event-ingestion   ← ROOT
trace da1dec82…   send FabEventIngestedV1   event-ingestion   ← ROOT
```

**And the work it causes is a separate root trace**, with no `parentSpanId`:

```
trace 5a5ace2e…
  receive FabEventIngestedV1          automation            ← ROOT, no parent
  send    OverlayHighlightRequestedV1 automation
  receive                             layout-composition
  receive                             audit-observability
```

So the downstream half of a journey is traced well. It is simply not attached to
what caused it.

### What was not established, and is not claimed

Whether `messaging.conversation_id` survives the hop. It is present on both
sides, but full-text search over traces did not filter usefully and it was not
worth more time once the parentage answer was clear. **Recorded as unresolved
rather than assumed either way.**

## 2. The consequence (T002) — and a conclusion the plan got wrong

The route is the **timestamp**, so Phase 2 stands.

But the plan's framing of *why* was imprecise, and the correction matters:

> **available or linked** → the leg is derivable from spans and T004–T006 are
> unnecessary

**That is wrong, and would have been wrong even if context had propagated.** A
histogram is recorded at a point in time by code that must know the elapsed
duration *then*. Trace correlation would let a human — or a query — join spans
after the fact; it cannot produce a distribution inside the running system.

So the acceptance moment must reach the applying service regardless. T001's
answer determined whether a *second* problem also exists. It does.

**Recorded because the plan asserted otherwise and a reader would inherit it.**
The question was still worth asking — it found #1750 — but its answer was never
going to delete Phase 2.

## 3. The bigger finding (T003) — filed as #1750

Every cross-service "what caused this?" question is currently unanswerable for
anything crossing the outbox, which is every integration event in the system.

Spec 023 spent a day on #1655's twelve-second first event reasoning from
wall-clock timings in test output, because the traces did not join up. It ended
with **no cause identified** and four candidates refuted. A joined trace might
have shown the answer directly.

**It may also be deliberate.** A message leaving the outbox minutes after the
request that queued it should arguably not extend that request's trace; the
recommended pattern is a span *link* rather than a parent. #1750 says so rather
than assuming a defect.

---

## Still to do

T004–T022. The route is settled; the measurement is not built.

---

## 4. The leg is measured (T007–T010)

`ILatencyBudget` joins `IEventBus` and `ITransactionalCommit` in `Shared.CQRS`:
an abstraction the Application layer may reference, implemented in
`ServiceDefaults` over the meter it owns. **That is the answer spec 024 could not
find** — it was right to refuse both a layering violation and a misleading
fragment, and wrong only in concluding there was no third option.

Spec 024's fragment segment is **deleted**, not left beside the real one. A
fragment reported against the leg's 200 ms budget would look like the leg
passing, and two segments would leave a wrong one to pick.

Both guards live in the implementation, so a second caller cannot forget them,
and both are tested at the meter with a `MeterListener` rather than a mock —
the property is that a measurement reaches the instrument, or does not:

| | |
|---|---|
| absent moment | records nothing (FR-005) — a zero is a perfect score for a journey nobody timed |
| negative duration | records nothing (FR-006) — a PTP-stepped clock, not a fast journey |
| real duration | **records** — or the guards are indistinguishable from an instrument that does nothing |

## 5. What could not be done, and why — T011, T012, T013

**The instrument's figure cannot be read from outside the process that records
it.** The histogram is emitted by SystemVariables and LayoutComposition; there is
no dashboard, and the Aspire MCP exposes traces, logs and resources but **no
metrics**. Nothing available here can read a percentile back.

That defeats three tasks:

- **T011** (a percentile obtainable without writing code) — **not met.** SC-001
  is therefore not met either.
- **T012 / T013** (reconcile against spec 022's independent figure) — **not
  performed.** There is no instrument reading to compare.

**So no figure from this instrument is quoted anywhere**, which is exactly what
T013 requires when reconciliation has not happened. Not in this note, not in the
PR, not in the constitution's table.

That restraint is the point rather than a shortfall. An instrument reporting a
plausible number for the wrong span cannot detect its own error, and this
programme has published an unverified figure before.

### What *is* known about correctness

Not nothing, and not a reconciliation either:

- The computation is `clock.UtcNow − rootIngestedAt`, where `rootIngestedAt` is
  `FabEventIngestedV1.IngestedAt` forwarded unchanged — the two ends ADR-0015
  names.
- Spec 024's split harness measures the same span externally at **187 ms and
  94 ms** warm. The instrument should land there. **Should is not measured.**

## 6. Cost and regression (T014–T016)

| | |
|---|---|
| warm total, before (spec 024) | 190, 263, 356, 519 ms |
| warm total, after | **206, 366 ms** |
| suite | **239 passed, 0 failed** |

No regression is detectable, and **no overhead figure is claimed**: the
difference is well inside run-to-run variance at this sample size, so the honest
statement is that the cost is below what this measurement can resolve — not that
it is zero. SC-005's 5% bound is not contradicted and not demonstrated.

## 7. §VII, half discharged (T017, T018)

The leg's row in constitution §IV now reads **"recorded, not yet readable"**,
with a note saying what that means. Measured in the sense that the number
exists; not in the sense that anyone can consult it.

**§VII is not satisfied for this leg**, and the table says so rather than
rounding up. The remaining half is #1707's ADR-0026 decision — building a
dashboard here would settle a Locked ADR by implementation.

ADR-0117 warned that a stale row exempts a leg by clerical error. This is the
first row to change, and it changed to something less flattering than "yes",
which is the only way that warning means anything.
