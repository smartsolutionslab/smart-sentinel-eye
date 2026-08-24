# Specification Quality Checklist: Read a single camera, and correct one

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-24
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

**All 16 items pass.** One marker existed and is resolved.

**FR-012 (renaming) was resolved by adoption, not by answer.** The question was
put to the user and the work was told to continue, so the recommendation —
address only, renaming out of scope — was adopted and is recorded in
Assumptions as *a decision to overturn rather than a consensus to cite*, the
same treatment spec 028 gave FR-008. A reviewer should read it as the open
question it is. If overturned, FR-012 inverts and US2 gains a second acceptance
path; the rest of the spec is unaffected, which is what makes it safe to adopt
provisionally rather than block on.

### Validation detail

**Deliberately kept out of spec.md, pinned by the contract instead** — status
codes, header names, route shapes. FR-006 and SC-003 are the borderline cases:
both are *security* requirements and needed to be sharp, so they are phrased as
"identical, verifiable field by field" rather than by naming a status number.
That keeps spec.md technology-agnostic without weakening the property, which is
what spec 015 lost when the requirement was withdrawn.

**Two withdrawn spec-015 requirements are resolved rather than inherited:**

| Spec 015 | Disposition here |
|---|---|
| FR-006 (indistinguishable refusal) | **Reinstated** as FR-006 — implementable the moment a single-camera read exists |
| SC-003 (field-by-field comparison) | **Reinstated** as SC-003 |
| FR-010 (ambiguous name across fabs) | **Superseded** — an identifier is never ambiguous; recorded as a consequence of the keying decision, to be revisited if a name-keyed lookup ever lands |

**Measurability of SC-002.** "No code path remains that filters a full listing
client-side to find one camera" is verifiable by inspection rather than by
measurement, which is weaker than the other criteria. Kept because the issue's
own acceptance criteria name it, and because SC-001 carries the quantitative
half of the same outcome.

**The keying decision is an assumption, not a clarification.** The user framed
identifier-vs-name as the central question; it is settled in Assumptions with
its evidence (spec 028 precedent, names non-unique over time, and the verified
fact that the listing and the frontend type already carry identifiers) and
marked as a decision to overturn. Punting it as a fourth question would have
been the more comfortable choice and the less useful one.
