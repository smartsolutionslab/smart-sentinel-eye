# Specification Quality Checklist: Two latency legs stop being exempt, and start being watched

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-25
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

Four validation calls worth recording. This spec was harder to keep
implementation-free than most, because the subject *is* instrumentation.

**No component, file or interface is named anywhere in the spec.** The
temptation was strong — the finding is specific to a named component in a named
directory, and the whole reason it went unnoticed is a path. The correction was
to describe the *shape* of the error ("a search scoped to one directory when the
capability lived in another", FR-004) rather than its coordinates. The
coordinates belong in the plan and in the correction itself.

**"Readable" is the load-bearing word and it is defined by contrast.** US4 and
SC-005 say what readable excludes — no debugger, no special build, no code
change — rather than naming a tool. That keeps it technology-agnostic while
leaving no room for a recording call to be reported as a discharged obligation.
The distinction is not invented here: the existing record already separates
"recorded" from "readable" for another leg and calls that state half discharged.

**FR-008 asserts an absence, and SC-004 says how to demonstrate it.** "Records
nothing" and "records zero" are indistinguishable to any test that checks a
figure exists, and the wrong one reports a perfect score for a journey nobody
observed. The requirement is phrased as the absence because that is what has to
be checked.

**SC-007 exists because this feature could plausibly fail halfway.** Both legs
need a live stream to measure, and it is genuinely unclear whether an automated
environment can supply one — the spec says so in Assumptions rather than
promising an automated check it may not be able to write. SC-007 requires the
end state of every leg to be stated explicitly, including any that remain
unwatched. Rounding a half-discharged leg up to "watched" would repeat exactly
the failure this feature exists to correct.

The central claim — that the kiosk decodes video and composites overlays onto it
— was verified in the code before the spec was written, down to the line that
renders the shared component and the transceiver that receives the track. Four
documents disagreed with it; none of them was checked against the code before
this.
