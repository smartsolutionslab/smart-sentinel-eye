<!--
  Smart Sentinel Eye PR template (ADR-031).
  PR title must follow Conventional Commits:
    <type>(<scope>): <subject>
  e.g.  feat(camera-catalog): register camera by RTSP URL

  Mandatory sections below — leaving any empty is a review blocker.
-->

## Linked

<!-- Spec(s) and ADR(s) this PR implements or amends. Use relative paths. -->
- Spec: `specs/NNN-<slug>/`
- ADR: `docs/adr/NNNN-<slug>.md`
- Task / issue: #

## Summary

<!-- 2-4 bullets: what changed and why. -->
-
-

## Latency budget impact

<!--
  Constitution §IV: PRs touching the event-to-overlay path must cite
  which leg they affect and the measured value vs the budget.
  Budget legs:
    1. Camera → SFU                          ≤ 80 ms
    2. SFU → kiosk decode                    ≤ 120 ms
    3. Presentation buffer (PTP)             ≤ 200 ms
    4. Event → overlay state                 ≤ 200 ms
    5. Composite + render                    ≤ 50 ms
    6. Headroom                              ≤ 150 ms
  If this PR is NOT on that path, write: N/A — <one-line reason>.
-->
- Leg(s) affected:
- Measured:           ms (budget        ms)
- Methodology:

## Test plan

- [ ] Unit tests
- [ ] Integration tests (Aspire fixture)
- [ ] Manual / exploratory
- [ ] N/A (docs-only / chore)

## Breaking change?

- [ ] Yes — explain migration path below
- [ ] No

<!-- If yes: -->
<!-- Migration: -->

---

<!--
  Reviewer checklist (don't delete; reviewer ticks as they go):
  - [ ] Title is Conventional Commits format
  - [ ] Scope matches a bounded context or allowed cross-cutting tag
  - [ ] No primitive obsession across context boundaries
  - [ ] No cross-context project references introduced
  - [ ] If this changes a public contract, version bumped & changelog noted
  - [ ] ADR exists for any policy/architecture deviation
-->
