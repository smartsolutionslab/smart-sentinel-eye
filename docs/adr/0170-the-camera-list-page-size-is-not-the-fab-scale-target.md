# ADR-0170: The camera list's page size is not the fab scale target

**Status:** Accepted
**Date:** 2026-10-11
**Supersedes:** —
**Superseded by:** —

## Context

`ListCamerasQueryHandler` refuses any request for more than
`ListCamerasDefaults.MaximumLimit = 200` rows — it rejects, it does not
clamp (spec 001 FR-007b). The constitution's deployment target (ADR-0159)
is 250 cameras, shared across a deployment's fabs. A reader comparing
these two numbers finds no recorded reason the cap sits below the
target, which reads as an oversight rather than a decision (#1983).

## Decision

**200 stays, as a page size, not a mirror of the scale target.**
`MaximumLimit` bounds a single response's size — a guard against an
unbounded result set — not the largest fab a caller is allowed to see.
Nothing should size it to the deployment target, because a caller
wanting the full set already has to page regardless of the exact
number: `includeRetired=true` accumulates retired rows past any target
over a deployment's lifetime, and a caller holding several fabs sees
their combined total, which the target was never meant to bound in one
response.

**The refusal (not a clamp) stays for the camera list.** A caller that
asks for more than 200 gets `CATALOG_LIMIT_EXCEEDED`, not a silently
truncated page.

**`ListDeadLettersQueryHandler`'s own cap (1 000, clamp instead of
refuse) is a different, already-accepted choice.** This ADR does not
unify the two. A repo-wide "caps clamp" or "caps refuse" convention is a
separate question nobody has asked; raising it later is a new decision,
not an inconsistency this one is failing to close.

## Consequences

**Positive:**
- The cap's reason is now on the record instead of reconstructed each
  time someone compares it to the deployment target.
- No code changes: the existing refusal behaviour, the 200 constant, and
  the matching frontend `MAXIMUM_PAGE_SIZE` are all already correct
  under this decision.

**Negative:**
- The two caps (200 refuse, 1 000 clamp) remain visibly inconsistent in
  shape across two contexts. Accepted, not fixed, by this ADR.

## Follow-up (doc-only, lane-eligible once this ADR lands)

- Correct `MaximumLimit`'s comment to cite this ADR instead of restating
  an invented reason.
- Fix "per fab" → "per deployment" in the constant's comment,
  `StreamFabAttributionOptions.cs:24`, and
  `CameraCatalogClientTests.cs:22` — ADR-0159 moved the target from
  per-fab to per-deployment on 2026-09-26, after these comments were
  written.
- Close #1983, citing this ADR.
