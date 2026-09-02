# Specification Quality Checklist: Primitives out of the domain, guards onto `Ensure`

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-02
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

### Validation iterations

**Iteration 1** found four failures, all of the same kind — the source
material was a measured code survey, so concrete symbols had leaked into the
requirements:

1. *No implementation details* — FR text named `Ensure.That`,
   `BannedSymbols.Guards.txt`, `RS0030`, `.editorconfig`, `DateTimeOffset`
   and `IsConcurrencyToken`. Rewritten to name the **obligation** rather than
   the mechanism ("a banned idiom MUST fail the build, not a review").
2. *Technology-agnostic success criteria* — SC text cited analyzer IDs and
   file names. Restated as observable outcomes (build fails at the desk in
   under a minute; migration diff is empty).
3. *Written for non-technical stakeholders* — user stories were framed as
   workstreams A–F. Reframed as six journeys, each with the reason it holds
   its priority.
4. *Scope clearly bounded* — no exclusions were stated. Added **Out of Scope**.

**Iteration 2**: all items pass.

### Deliberate deviations, with reasons

- **The Context section names ADRs, file paths and counts.** This is
  orienting evidence, not requirement text, and this repository's convention
  is that a claim about drift must carry the evidence that establishes it —
  otherwise the correction is itself taken on trust. The requirements above
  it stay mechanism-free.
- **Some domain vocabulary is unavoidable.** "Aggregate", "value object" and
  "optimistic concurrency" are this project's ubiquitous language, named in
  the constitution. Avoiding them would obscure rather than clarify.

### Zero clarification markers — the four judgement calls, and their defaults

No `[NEEDS CLARIFICATION]` markers were raised. Four points were genuinely
open; each had a defensible default drawn from existing precedent, recorded
in **Assumptions** rather than deferred as a question:

| Open point | Default taken | Precedent |
|---|---|---|
| Shared timestamp base vs. per-context types | Per-context | The two existing timestamp types are per-context; ADR-0046 is maximalist hand-written |
| Do timestamps validate anything? | Normalize only, no new guard overload | Existing timestamp types normalize to UTC and validate nothing |
| Do opaque payloads get a type at all? | Exempt from parsing, not from non-emptiness and a size bound | The exemption's stated reason is that the content is uninterpreted, which the narrower reading preserves |
| Coverage threshold before retyping | Covering tests added first where absent on a retyped path | Otherwise "green throughout" is guaranteed by absent coverage rather than correctness — FR-024 |

Any of these four can be overridden in Phase 2 without reopening the spec.

### One risk the spec records but cannot resolve

Story 5's converted concurrency token is the single technical unknown. FR-023
handles it by sequencing rather than by prediction: prove it on one aggregate
before the other nine. If it proves unworkable, Stories 1–4 and 6 are already
banked and Story 5 can be dropped without unpicking them.
