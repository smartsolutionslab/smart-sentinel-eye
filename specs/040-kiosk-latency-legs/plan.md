# Implementation Plan: Two latency legs stop being exempt, and start being watched

**Branch**: `040-kiosk-latency-legs` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/040-kiosk-latency-legs/spec.md`

---

## Summary

Correct the record in four places, then measure the two legs the correction makes
subject to §VII. The kiosk times both, reports them to a service, and the service
records them through the meter that already feeds the dashboard.

**One of the two legs comes out partly discharged, and the plan says so rather
than rounding up.**

### What Phase 0 changed about this plan

- **The decode leg is not directly observable** (research §4). No WebRTC
  statistic is *SFU → kiosk decode*; the one that would close the gap depends on
  the PTP leg that does not exist. The honest fragment —
  `totalProcessingDelay + totalDecodeTime` — gets recorded under a name that says
  what it is, and §IV records the leg as measured **in part**. This is spec 024's
  refusal applied a second time.
- **Neither leg can be verified in CI, by design** (research §2). `camera-sim`,
  `scenario-simulator` and the ICE host-publishing all sit inside
  `if (isRunMode && !isE2ETests)`. Verification is a written manual procedure.
- **The browser reports to a service rather than emitting** (research §3). It is
  given no OTLP endpoint — only the gateway, Keycloak and the hub — and reporting
  keeps ADR-0118's single sink intact rather than working around it.
- **An ADR is needed after all**: 0122, for the emitter question ADR-0118 never
  faced.

---

## Technical Context

**Language/Version**: TypeScript 5.7 / React 19 (kiosk + shared); C# / .NET 10
(the receiving endpoint and the meter).

**Primary Dependencies**: none added. No OpenTelemetry JS SDK — the browser posts
a number, it does not export telemetry.

**Storage**: none. Measurements are metrics, not rows.

**Testing**: Vitest for the browser side; xUnit for the endpoint and the
recording guards; **a written manual procedure** for the two numbers themselves.

**Target Platform**: `kiosk-web` in run mode. Not CI (research §2).

**Project Type**: frontend measurement + one service endpoint + documentation
correction.

**Performance Goals**: **FR-012** — the observer must not eat the 50 ms budget it
observes. Two animation-frame callbacks and a subtraction, on a path that already
re-renders.

**Constraints**: no behaviour change to the kiosk (**FR-011**); no new sink
(ADR-0118); the two figures must stay separable (**FR-007**).

**Scale/Scope**: 4 documents corrected, 1 ADR, ~2 browser modules, 1 endpoint,
1 meter method, ~10 tests, 1 manual procedure. Two legs measured, one of them
partly.

---

## Constitution Check

*GATE: must pass before Phase 0. Re-checked after Phase 1 — see below.*

| Principle | Verdict | Note |
|---|---|---|
| **II. DDD with value objects** | **Pass** | The reported measurement crosses a trust boundary as a DTO and is validated there. Nothing enters a domain model — a latency figure is telemetry, not domain state. |
| **III. Bounded context isolation** | **Pass, with a decision to make in Phase 1** | The endpoint belongs to whichever context owns it; it must not become a ninth context nor a cross-context reference. The natural home is the context that already owns the kiosk's stream — settled in the contract. |
| **IV. Latency budget** | **This feature is about §IV** | It changes no leg's behaviour and no budget. It corrects the record of which legs are built and adds numbers for two of them. |
| **V. Spec-driven** | **Pass** | Spec → plan → tasks → implementation. |
| **VII. Observability** | **The obligation being discharged, and only partly** | See below. |
| **VIII. Safe at trust boundaries** | **Pass, and it needs care** | A browser-reported number is untrusted input: it is client-supplied, and a malicious or broken kiosk could report anything. Validated at the boundary, and the guards that already exist server-side (FR-008/FR-009) do most of that work. |
| **IX. No speculative generality** | **Pass** | One measurement path, two callers, no framework. No OpenTelemetry JS SDK — that would be infrastructure for a need that does not exist. |

### §VII, stated honestly

The feature discharges §VII **fully for composite + render** and **partly for
decode**.

Decode is measured in part because the leg as ADR-0015 defines it cannot be
observed without the PTP leg that is unbuilt (research §4). §IV must therefore
record it in the vocabulary already used for #1707 — measured in part, and the
column says so rather than rounding up. **SC-007 exists for exactly this
outcome** and requires it be stated, not papered over.

Anyone reading the corrected table will see one leg fully watched, one partly, one
still unbuilt, and one still recorded-not-readable. That is four different states
across six legs, and the table's job is to keep them distinguishable.

**Post-Phase-1 re-check**: unchanged.

---

## Project Structure

### Documentation (this feature)

```text
specs/040-kiosk-latency-legs/
├── spec.md
├── plan.md                       # this file
├── research.md                   # Phase 0 — eight findings
├── contracts/
│   ├── the-two-measurements.md       # what each figure is, named honestly
│   └── the-corrected-record.md       # every document, and its corrected text
├── quickstart.md                 # the manual procedure CI cannot replace
└── checklists/requirements.md
```

**No `data-model.md`.** No entity, field or stored state. The reported
measurement is a transport shape and lives in `contracts/`.

### Source code

```text
docs/adr/0122-browser-measurements-enter-through-a-service.md   # NEW

.specify/memory/constitution.md                  # §IV table — the load-bearing fix
CLAUDE.md                                        # latency section
specs/024-latency-budget-visible/verification.md # §6, where the error started

apps/shared/src/observability/
  kioskLatency.ts                                # NEW — time both legs, report
  kioskLatency.test.ts                           # NEW — the guards, without a stream
apps/shared/src/ui/composites/
  CameraViewer.tsx                               # observe only; behaviour unchanged
apps/kiosk-web/src/features/cell/CellPage.tsx    # overlay-change timestamp

src/<owning-context>/Api/                        # the receiving endpoint
src/Shared.CQRS/ILatencyBudget.cs                # + the two kiosk legs
src/ServiceDefaults/LatencyBudget.cs             # + their implementation
```

---

## Phase 1 — Design

### The two measurements

Given precisely in
[contracts/the-two-measurements.md](./contracts/the-two-measurements.md),
including the name each carries and why the decode one does not claim its budget.

### The transport

The kiosk computes an elapsed time and **posts the number**. It does not post a
start, so the network hop cannot corrupt the figure — a slow post makes the report
late, never the measurement large.

The service records it through `ILatencyBudget`, which is where **FR-008** and
**FR-009** are enforced: nothing recorded when the start is unknown, nothing
recorded for a negative or implausible elapsed time. Both guards already exist
there with their reasons written down, and putting the kiosk's legs behind the
same interface is what stops a second caller forgetting them.

**The browser side must also apply them**, because a figure that fails a guard
should not be sent at all — but the server side is where they are *enforced*, since
the browser is untrusted (§VIII).

### The correction

[contracts/the-corrected-record.md](./contracts/the-corrected-record.md) gives all
four documents and their replacement text. It is a contract rather than a task
note because **the four must agree afterwards**, and four separate paraphrases of
one correction is how they disagreed in the first place.

Each records **why** the error happened (**FR-004**) — a search scoped to
`apps/kiosk-web` when the capability lives in `apps/shared` — because the
mechanism generalises and the correction does not.

### Testing strategy

**Automated, and it covers everything except the numbers**: the guards reject what
they must; the two figures stay separable; the reporting shape is right; the
endpoint validates and records; the corrected table says what it must.

**Manual, because CI has no stream** (research §2): the two numbers themselves,
following [quickstart.md](./quickstart.md) against the run-mode stack.

Saying which is which matters more than usual here. A test suite that appeared to
cover these legs while running without video would be worse than none — it is the
same class of claim as a document saying a leg is unbuilt when it runs on every
kiosk.

---

## Risks

**1. The decode figure gets named as the leg.** It is a fragment, and a fragment
reported against a 120 ms budget looks like the budget passing. Mitigated by the
name, by the contract stating it, and by §IV recording the leg as measured in
part.

**2. The correction lands in one document.** Four repeat the claim. One corrected
and three not is the same failure with a smaller blast radius. Mitigated by the
contract holding all four together.

**3. The measurement is verified by a green test suite that never saw a frame.**
CI cannot produce video and the tests that run there prove the guards, not the
legs. Mitigated by the quickstart being a required part of verification rather
than a suggestion, and by the PR stating which claims rest on it.

**4. A browser-reported number is trusted.** It is client-supplied and a broken
kiosk could report anything. Mitigated by validating at the endpoint and by the
guards being enforced server-side rather than only in the browser.
