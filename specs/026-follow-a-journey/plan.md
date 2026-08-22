# Implementation Plan: A cross-service journey can be followed end to end

**Branch**: `026-follow-a-journey` · **Spec**: [spec.md](./spec.md) ·
**Date**: 2026-08-22 · **Issue**: #1750

## Summary

Make the causal chain survive the outbox, so "what caused this?" becomes
answerable across services.

Phase 0 changed the plan twice over. It found a **supported extension point**
that makes the send side a configuration change rather than an invention — and it
found that **the spec's central argument was wrong**, which reopens a design
decision the spec had treated as settled.

The consequence is a smaller feature than specified, tried in a different order.

## Technical Context

**Language**: C# 13 / .NET 10 · **Messaging**: Wolverine 6.24.2 over RabbitMQ
with a Postgres outbox · **Telemetry**: OpenTelemetry → the Aspire dashboard
(ADR-0118) · **Testing**: xUnit + the Aspire fixture

**Constraints**: no reported duration may grow (SC-003); the library's own tables
are not ours (FR-011); the existing latency measurement must be untouched and
must not start depending on telemetry (FR-010); the result must be **followable
by a person in the dashboard**, not merely emitted (FR-008, SC-007).

## Constitution Check

| Principle | Status |
|---|---|
| I. On-prem first | Unaffected. |
| II. DDD with value objects | Unaffected — transport metadata, not domain state. |
| III. Bounded context isolation | Respected: the change is in `ServiceDefaults`, which every context already uses. |
| IV. Latency budget | **Guarded, not served.** FR-009 and SC-006 keep this from costing latency; it does not make a leg faster. |
| V. Spec-driven development | Followed, including correcting the spec when Phase 0 contradicted it. |
| VI. Aspire is the composition root | Unaffected. |
| VII. Observability is non-negotiable | Directly served. §VII's dashboards are about latency legs; this is the causality half of the same principle, and the one that made spec 023's investigation fail. |
| VIII. Safe at trust boundaries | **Worth a check.** Anything stamped onto every outgoing message is visible to anything that reads the broker. Trace identifiers are opaque and carry no business data, and the plan keeps it that way. |
| IX. Forward-compatible interfaces | Respected — headers are additive and a message without them behaves as today. |

**No exception requested.**

## Approach

### 1. Correct the record first

Done in Phase 0, and listed as a step because it is work rather than tidying:
the spec's US3 justification claimed parentage would report "a twenty-millisecond
journey as eight minutes, in every percentile". Span duration does not work that
way — a span measures its own start to its own end, and percentiles are computed
over spans. The requirement stands; the reasoning was wrong and is marked as
corrected in both documents.

**Left uncorrected, that argument would have ruled out the cheapest option on
false grounds.**

### 2. Try the cheap route before the clever one

`WolverineOptions.MetadataRules` takes `IEnvelopeRule`, applied to outgoing
envelopes — the same mechanism Wolverine uses for tenancy and delivery windows.
`Envelope.Headers` serialises with the message.

Since `Envelope.CorrelationId` and `ParentId` already exist and
`WolverineTracing.StartReceiving` already reads the envelope, **stamping the
context through headers may join the journey up with no custom span code at
all.** That is the smallest change that could work, and it uses the library as
designed rather than around it.

If it works, this feature is a rule and its tests.

### 3. Only then consider links

Three arguments for links survive Phase 0, and all are smaller than the one that
did not: fan-in (one handler, several causes — which this system does not
currently do), sampling decisions travelling from a minutes-old context, and
trace lists sorted by duration becoming dominated by queue time.

**The third is measurable and is the one to check**: if every trace is minutes
long, the dashboard becomes hard to use and links earn their extra code. That is
an observation to make after step 2, not a prediction to build on.

### 4. Verify a person can follow it

FR-008 and SC-007 ask for a human reading the dashboard, not a test asserting a
link exists in memory. Spec 024 registered a trace source and could not confirm
spans arrived for two days; that is the failure this step exists to avoid.

### 5. Confirm nothing got slower or longer

`SC-003` (no reported duration grows) and `SC-006` (steady state no worse),
measured the same way as specs 022, 024 and 025. Headers on every message are not
free, and "not free" is a measurement rather than a shrug.

## Project Structure

### Documentation

```
specs/026-follow-a-journey/
├── spec.md              ← carries a correction from Phase 0
├── research.md          ← Phase 0, complete
├── plan.md              ← this file
├── quickstart.md
├── tasks.md
├── verification.md
└── checklists/requirements.md
```

No `data-model.md` and no `contracts/`: nothing here is a domain model, and the
message contracts are untouched — the context rides in envelope headers rather
than in any `Shared.Contracts` type. That is a deliberate difference from spec
025, which needed a contract field because a *metric* must be computed in-process
where telemetry cannot help.

### Source code

```
src/ServiceDefaults/WolverineDefaults.cs   the metadata rule, registered once
src/ServiceDefaults/                       a rule type, if it needs a name
tests/Integration.Tests/                   the journey, followed
```

Expected to be small. If it grows past this, step 2 failed and step 3 is running,
which is worth noticing rather than absorbing.

## Complexity Tracking

No constitutional exception. One item for a reviewer:

| Item | Why | Why proportionate |
|---|---|---|
| A header on every outgoing message | The outbox has nowhere else to keep the context, and the library's tables are not ours | Additive, opaque, and small; a message without it behaves exactly as today |

## Risks

**Taking the link route because the spec said so.** The spec's argument for it
was wrong, and a plan that inherited it would have built custom span code to
avoid a problem that does not exist. Step 2 exists to stop that.

**Confirming it works without checking a person can use it.** The precedent is
spec 024, which registered a source and could not verify spans for two days. A
link or parent that nobody can follow in the dashboard is the same as none.

**Stamping something sensitive.** Trace identifiers are opaque; the rule must not
grow into carrying business data onto a broker every service can read.

**Assuming this fixes #1655.** It does not. It makes the investigation that
failed there possible, which is a different claim and the one to make.
