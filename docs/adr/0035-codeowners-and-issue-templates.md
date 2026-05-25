# ADR-0035: CODEOWNERS and Issue Templates

**Status:** Accepted
**Date:** 2026-05-25

## Context

Two small but high-leverage collaboration aids:

- **CODEOWNERS** tells GitHub who to auto-request reviews from when a
  PR touches a particular path. It also lets us enforce per-area
  ownership later.
- **Issue templates** give bug reports, feature proposals, and ADR
  proposals a consistent shape so triage is mechanical.

Without these, every contributor invents their own structure and
triage cost rises.

## Decision

### CODEOWNERS

`.github/CODEOWNERS` assigns `* @notonlywhite` for v1 (single owner).
The file includes commented-out scaffolding for per-context ownership
(one block per bounded context, plus `apps/web/`, `deploy/`,
`.github/`) so the structure is in place when additional reviewers
join. Each block points to the appropriate `*` until that role is
filled.

### Issue templates

Three GitHub Issue Forms templates under `.github/ISSUE_TEMPLATE/`:

| Template | When to use | Required fields |
|---|---|---|
| **Bug report** (`bug_report.yml`) | Something is broken or unexpected. | Summary, steps, expected/actual, latency-sensitive flag, environment. |
| **Feature request** (`feature_request.yml`) | Propose a capability or substantial change. Triage may convert this into a `/speckit-specify` flow. | Problem, proposed approach, constitution/ADR alignment, latency impact, alternatives. |
| **ADR proposal** (`adr_proposal.yml`) | Propose a new ADR or supersede one. | Context, decision, consequences, alternatives, supersedes. |

A `.github/ISSUE_TEMPLATE/config.yml` redirects open-ended questions
to **GitHub Discussions** and disables blank issues. Bug reports
default to label `type:bug`, feature requests to `type:feature`, ADR
proposals to `type:adr`. All three start with `needs-triage`.

## Consequences

- **Positive:** every issue arrives with the context needed for
  triage. Reduces back-and-forth.
- **Positive:** CODEOWNERS is in place when we add the second
  engineer — no scramble.
- **Negative:** structured templates are slightly more friction than
  a blank text box. The constitution-alignment field on feature
  requests is the most likely to be skipped; we accept that as a
  trade-off for traceability.

## Alternatives Considered

- **No templates** (free-form issues): rejected — high triage cost.
- **Single combined template**: rejected — bugs and ADR proposals
  have radically different signal needs.
- **CODEOWNERS deferred until team grows**: rejected — the empty file
  is near-zero cost and the placeholder structure documents intended
  ownership.

## Implementation Notes

- CODEOWNERS and the three issue templates ship together in the same
  PR that introduces this ADR.
- Update CODEOWNERS whenever a new bounded context or top-level
  directory is added.
