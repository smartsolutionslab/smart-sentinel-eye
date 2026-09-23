# Plan 230 — The orphan the vocabulary keeps

**Spec:** [spec.md](./spec.md) · **Issue:** #2503 · **Colour:** characterisation, observed green

## Bounded context and layers

AuditObservability, Domain layer (`ResourceKind`). **No layer changes.** The work is a
recorded decision plus a characterisation run.

## Entities, value objects and invariants

| Type | Invariant, unchanged |
|---|---|
| `ResourceKind` (VO, `IValueObject<string>`) | `Value ∈ All`, and `From` throws `ArgumentException` otherwise |
| `ResourceKind.All` | a closed list of 11 members, the public API surface of `GET /audit/{resourceKind}/...` and of `?resourceKind=` |

No invariant is added. This spec considered and rejected "every member of `All` has
a producer" (spec §2, Rejected), because `Rule` already violates it and no consumer
needs it.

## Messaging

None. No domain or integration event is added or changed. `V1ResourceMap` is untouched.

## Boundary rules

No project references change. No `Shared.Contracts` change.

## Constitution and ADR alignment

- **ADR-0144**: this spec makes no new architecture decision. It chooses the
  zero-behaviour-change option under the existing closed-vocabulary rule (spec 009
  FR-009).
- **ADR-0036**: the smallest change, which here is nothing.
- **ADR-0139 / ADR-0144 phase 4a**: characterisation. The covering tests are
  `ResourceKindTests.Accepts_every_member_of_the_v1_vocabulary(member: "webhook")` and
  `ResourceKindTests.All_returns_the_full_vocabulary`. They exist, pin the exact
  behaviour that removal would change, and must be observed green, unmodified.
- **Constitution §IV**: N/A, off the latency path.

## Coordination with #2504 (spec 226)

It edits `ResourceKind.cs` (the `Rule` comment) and `V1ResourceMap*.cs`. This spec
edits neither, so it can merge in either order with no rebase conflict.

## Risks

- **A later spec removes `Webhook` without reading this spec.** This is mitigated by
  the existing source comment, which already states *why* it stays. The
  `All.Count == 11` assertion also fails loudly on any removal.
