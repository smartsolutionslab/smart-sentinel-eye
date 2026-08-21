# Implementation Plan: Every leg of the latency budget can be watched

**Branch**: `024-latency-budget-visible` · **Spec**: [spec.md](./spec.md) ·
**Date**: 2026-08-21 · **Issue**: #1681

## Summary

Make the latency budget measurable instead of asserted. Phase 0 found the
feature is smaller and stranger than #1681 implied: **two of six legs can be
measured, three do not exist to measure, and one is arithmetic.**

That is not a reason to shrink the ambition — it is the answer to the question
the issue asked. §VII says a leg without a dashboard cannot ship; it turns out
three of those legs have not shipped either.

## Technical Context

**Language**: C# 13 / .NET 10, TypeScript · **Telemetry**: OpenTelemetry → OTLP,
already configured in `ServiceDefaults` for metrics and traces ·
**Composition**: Aspire AppHost (§VI) · **Testing**: xUnit + Aspire fixture

**Performance goal**: instrumentation under 5% of the measured leg's budget
(SC-004), steady-state latency unmoved (SC-006, FR-012).

**Constraints**: no leg left unaddressed and unmentioned (FR-007); every figure
states what it does not establish (FR-009); the ADR-0026 decision is presented,
not made (FR-011).

## Constitution Check

| Principle | Status |
|---|---|
| I. On-prem first | Unaffected. |
| II. DDD with value objects | Unaffected — instrumentation, not domain. |
| III. Bounded context isolation | Respected: the instrument lives in `ServiceDefaults`, which every context already uses. No cross-context reference. |
| IV. **The latency budget is sacred** | **The feature exists to make §IV enforceable.** It also reveals that §IV describes a path not yet built end to end. |
| V. Spec-driven development | Followed. |
| VI. **Aspire is the composition root** | Any collector or Grafana stack is an AppHost resource. The MediaMTX metrics change is config on an existing resource. |
| VII. **Observability is non-negotiable** | Currently unmet across all six legs; this is the correction, and it will not fully close it. |
| VIII. Safe at trust boundaries | Unaffected. |
| IX. Forward-compatible interfaces | Unaffected. |

### Two things a reviewer must decide, not the implementer

**ADR-0026 is Locked and describes something that does not exist.** Its
comparison phase — a collector fanning to both the Aspire dashboard and a Grafana
stack, with a sunset clause — never started. Enact, amend, or split (research.md
Q2). The plan takes the technical path common to all three and stops there.

**§VII will still be unmet when this feature ends.** Two legs measured, three
unmeasurable because unbuilt, one arithmetic. Whether that satisfies "a leg
without a dashboard cannot ship" — for legs that have not shipped — is a
constitutional reading, and the honest options are to amend §VII, to accept the
gap with a recorded reason, or to treat the unbuilt legs as blocking. **No
exception is requested here**; the situation is reported for judgement.

## Approach

### 1. Measure the one leg that is implemented and budgeted

`event → overlay state ≤ 200 ms`. Record its duration as a histogram through the
metrics pipeline `ServiceDefaults` already configures, so a percentile exists
rather than a most-recent value.

The measurement runs from the event being accepted to its effect being applied.
It **excludes** delivery to a kiosk — legs 2, 3 and 5 — and the note says so,
because a figure that silently means less than its name is how this programme
keeps getting caught.

### 2. Turn on the leg that is already measurable

MediaMTX supports Prometheus metrics and the AppHost does not enable them. A
config change makes `camera → SFU` visible. Cheap, and it doubles the number of
legs that can be seen.

### 3. Establish the cost before claiming there is none

Before/after on the warm path — **with the exporter's state confirmed first**.
Spec 023 recorded the same comparison and then had to admit it might be vacuous,
because the OTLP exporter only attaches when `OTEL_EXPORTER_OTLP_ENDPOINT` is
set and nobody checked whether the fixture sets it. That ambiguity is not
inherited.

### 4. Write down every leg that is not measured, and why

Four of six. Three because the code does not exist; one because it is the
remainder of a subtraction. FR-007 makes this a deliverable rather than an
omission, and on the evidence it is the largest single piece of the feature's
output.

### 5. Give a PR author something to cite (§IV)

A repeatable procedure producing a figure for the event-to-overlay leg, with its
caveats attached. Spec 022's and spec 023's harnesses already produce numbers;
what is missing is a written way to obtain one and a statement of what it means.

### 6. Raise the product finding separately

That the 800 ms path is not assembled end to end is a fact about the product,
discovered here and belonging elsewhere — #1655's precedent, where a measurement
feature filed what it found instead of absorbing it.

## Project Structure

### Documentation

```
specs/024-latency-budget-visible/
├── spec.md
├── research.md          ← Phase 0, complete
├── plan.md              ← this file
├── quickstart.md        ← Phase 1
├── tasks.md             ← /speckit-tasks
├── verification.md      ← Phase 5
└── checklists/requirements.md
```

No `data-model.md` — a histogram is not a model. No `contracts/` — no interface
changes are anticipated; if one proves necessary that is a finding to raise.

### Source code

```
src/ServiceDefaults/            the latency instrument, registered once for every context
src/AppHost/Resources/mediamtx.yml   enable metrics on the SFU
src/AppHost/AppHost.cs          expose them, if a scrape target is needed
tests/Integration.Tests/        measurement of the instrument's own cost
docs/adr/                       ADR-0026 amendment, if the reviewer chooses one
```

## Complexity Tracking

No constitutional exception requested. One item for a reviewer's eye:

| Item | Why it is here | Why it is proportionate |
|---|---|---|
| Instrument in `ServiceDefaults` rather than in EventIngestion | The leg spans four services; instrumenting one end of it in one context measures a fragment | Same placement as spec 023's trace source, for the same reason |

## Risks

**The measurement is easy and the conclusion is uncomfortable.** The likely
outcome is two legs visible, four documented as not, and §VII still unmet. A
feature that closes an issue by explaining why it cannot be closed needs saying
plainly at the end, not discovering at review.

**Instrumenting a fragment and calling it the leg.** The leg is defined as
RabbitMQ + projection. A histogram around one handler would produce a number
that looks like the budget and is not. The measurement must span what ADR-0015
says it spans, or say what it actually spans.

**Cheap observation is a claim, not a fact.** SC-004's 5% bound exists because
the instinct is to treat instrumentation as free. It is measured.

**Enabling MediaMTX metrics changes a running media server's configuration.**
Small, but it is the streaming path, and a mistake there is visible to anyone
watching a camera.
