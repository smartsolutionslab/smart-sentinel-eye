# ADR-0031: Pull Request Template and Title Convention

**Status:** Accepted
**Date:** 2026-05-25

## Context

Open-ended PR descriptions lead to under-documented changes and
missed review concerns — especially around the load-bearing latency
budget (constitution §IV) and the spec-driven traceability requirement
(constitution §V). A structured template that PR authors fill in by
default makes those concerns hard to skip.

## Decision

**PR title** matches the squashed Conventional Commit message
(ADR-0030): `<type>(<scope>): <subject>`.

**PR body** uses `.github/pull_request_template.md` with the following
**mandatory** sections:

| Section | Required content |
|---|---|
| Linked | Path(s) to spec(s) and ADR(s) the PR implements or amends. |
| Summary | 2–4 bullets — what changed and why. |
| Latency budget impact | Which leg(s) of the latency budget are affected, measured value vs budget, or `N/A — <reason>` if not on the streaming/overlay path. |
| Test plan | Checklist of what was tested (unit, integration, manual). |
| Breaking change? | `[ ] yes  [x] no` — if yes, include migration notes. |

Reviewers may also request additional context. **Missing mandatory
sections is a review blocker**, regardless of CI status.

## Consequences

- **Positive:** every PR provides traceability back to the spec and
  ADR layer.
- **Positive:** the latency budget is impossible to silently erode —
  authors must opt out explicitly (`N/A`) and reviewers see it.
- **Positive:** test plan visibility helps reviewers understand
  coverage without reading every test file.
- **Negative:** small upfront cost per PR (~1 minute). Offset by
  faster review cycles.

## Alternatives Considered

- **Free-form title + body**: rejected — invites drift.
- **Spec-Kit feature-ID prefix (`[NNN-feature] subject`)**: rejected
  because it does not compose with the Conventional Commit history on
  `develop`.
- **Title only, no template**: rejected — would lose the latency-budget
  forcing function.

## Implementation Notes

The template lives at `.github/pull_request_template.md`. Update both
this ADR and the template if mandatory sections change.
