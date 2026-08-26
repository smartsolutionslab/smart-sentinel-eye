# Specification Quality Checklist: Every identity can say who it is

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-26
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
- [x] User scenarios cover strategic flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

Four validation calls worth recording.

**No product, protocol, claim or scope name appears anywhere in the spec.** The
subject is a single missing field in a configuration file, and naming it would
have produced a one-line change request rather than a specification. The
vocabulary used instead — *identity*, *naming piece*, *permission list*,
*attribution* — is the distinction a non-technical reader needs in order to judge
whether the change is right: **who is acting, can the system say so, and what
happens when it cannot.**

**The refusal is presented as correct, not as the bug.** The tempting shape was
"the system rejects valid requests". It does, and it should: attributing a change
to a fabricated person would corrupt the audit trail, and the code says so where
it is enforced. Framing the safety property as the defect would have invited a
fix that softened it. The defect is that most identities cannot be named, so the
net catches things it was never meant to catch.

**US2 is not cosmetic and the spec insists on it.** Thirty-two startup warnings
naming things that do not exist could be dismissed as noise. They are the system
having already reported this defect, on every boot, to nobody. A fix that made
the identities nameable and left four fictional entries on each of them would
repair the instance and preserve the mechanism — and the mechanism is what
produces the next one.

**The service accounts are given the claim they do not need, on purpose.** The
precise answer is that only identities acting for a person require attribution.
The spec rejects it, and says why: *which* identities need it is a judgement
that must be remade every time one is added, and an error in that judgement is
invisible until the first write. A rule with no exceptions is the one FR-006 can
actually check. That is a deliberate trade of precision for verifiability, and it
belongs in the record rather than in a reviewer's head.

Everything the spec asserts was measured before it was written: the realm was
imported into a throwaway directory service, tokens were minted for every
identity in turn, and the six that cannot name themselves were read one at a
time rather than inferred from the file.
