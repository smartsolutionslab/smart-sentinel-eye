# Specification Quality Checklist: The configuration stops discarding what it says

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
- [x] User scenarios cover the strategic flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

### The first draft was wrong, and how

**It claimed six of eight identities could not name their holder. One cannot.**

The claim was read off the configuration file — which identities carry the
mapper — and this checklist then asserted that *"tokens were minted for every
identity in turn"*. They had not been. Four had been minted earlier in the day
for a different feature; the other four were inferred.

Measured properly during planning, one credential per identity:

| Identity | Names its holder | Why |
|---|---|---|
| operator console (in use) | yes | inherits it from a broad permission |
| wall display | yes | hand-added copy, last week |
| **operator console (replacement)** | **no** | nothing supplies it |
| the five background workers | yes | a property of how they sign in |
| a wall display enrolled at runtime | yes | same |

**Background workers never needed configuring**, because the way they sign in
carries the holder's identity inherently. No file records that, which is why the
file appeared to say otherwise.

This error is left in the spec's Assumptions rather than deleted. It is the same
mistake the configuration itself makes — asserting from a file what only a
measurement settles — committed while writing a specification about exactly that,
one day after two features were spent correcting other instances of it.

### What the rewrite changed

**The headline moved from the count to the mechanism.** "Most identities are
broken" was false and would not have survived planning. "The configuration
discards four entries per identity on every start, says so thirty-two times, and
that silence has hidden two defects in two weeks" is true, is the reason both
defects were expensive, and is what the feature actually addresses.

**One identity is still broken and it still matters** — the unused replacement
for the operator console, which would refuse all seventeen kinds of attributed
change the moment anyone adopted it. That is now US2 rather than the whole story.

**FR-011 is new and exists because of the error.** The reason background workers
are unaffected was undocumented, is not visible in any file, and looks like luck.
Writing it down is what stops the next reader counting them as broken — as this
spec's own author did.

### Validation calls

**No product, protocol, claim or scope name appears anywhere.** The subject is
one missing field and four fictional list entries; naming them would have made
this a change request. The vocabulary — *identity*, *naming piece*, *permission
list*, *attribution* — is what a non-technical reader needs to judge the change:
**who is acting, can the system say so, and what happens when it cannot.**

**The refusal is presented as correct, not as the bug.** The system rejects
changes it cannot attribute, and should: attributing to a fabricated person would
corrupt the audit trail. Framing that as the defect would invite a fix that
softened it.

**US1 outranks the broken identity, deliberately.** Fixing the one identity and
leaving thirty-two discarded entries would repair an instance and keep the
mechanism — and the mechanism has now produced two.
