# ADR-0037: Guided Phased Development Process

**Status:** Accepted
**Date:** 2026-05-25

## Context

Spec-Kit gives us a backbone:
`/speckit-constitution → specify → clarify → plan → tasks → implement`.
It is excellent for *what to build* and *how to break it down*, but
treats implementation as a single block — testing and review happen
inside `/speckit-implement` rather than as separately observable gates.

For a 24/7 industrial product under the constitution's latency SLO
and on-prem-first posture, we want explicit checkpoints between
"code is written" and "code can merge to `develop`". These
checkpoints are also where a human operator (the architect lead)
decides whether the agent is on track without re-reading every file.

## Decision

Adopt a **seven-phase guided development process** for every feature
or non-trivial change. Each phase produces a concrete artifact and is
**gated by explicit user approval** before the next phase begins.
Claude Code does not autonomously advance past a gate.

| # | Phase | Command(s) | Artifact | Gate |
|---|---|---|---|---|
| 1 | **Specify** | `/speckit-specify` (+ optional `/speckit-clarify`) | `specs/NNN-x/spec.md` | Spec reviewed; no unresolved `[NEEDS CLARIFICATION]`. |
| 2 | **Plan** | `/speckit-plan` | `specs/NNN-x/plan.md` | Plan aligns with constitution + ADRs; no off-stack choices. |
| 3 | **Tasks** | `/speckit-tasks` + `/speckit-taskstoissues` | `specs/NNN-x/tasks.md`; GitHub issues on Project #13 | Tasks atomic and independently testable; pushed to board. |
| 4 | **Implement** | `/speckit-implement` (Karpathy-guided, ADR-0036) | Code + tests; format clean; analyzers clean | Per-task tests green; analyzers green; commits follow ADR-0030. |
| 5 | **Verify** | `/verify` (or explicit run/test) | Verification report comment on the PR | Behaviour observed end-to-end. For UI: run the app and click through golden + edge paths. For backend: integration tests via Aspire AppHost + Testcontainers green. For latency-sensitive code: measured latency cited per leg vs budget. |
| 6 | **QA / Review** | `/code-review`; `/security-review` if security-sensitive | Findings list, each addressed or explicitly accepted | All findings resolved or carry a written rationale in the PR. |
| 7 | **PR** | `gh pr create` with template; respond to CI | PR open against `develop`, template fully filled, CI green | Reviewer approval + green CI + admin merge (or peer merge once team grows). |

**Karpathy guidelines (ADR-0036)** are invoked automatically during
phases 4–6.

**Skipping a phase** is allowed for trivial changes (typo fix,
dependency bump, comment-only) by writing `Phase X: skipped — <one
line reason>` in the PR body. Documentation-only changes typically
skip phases 5 and 6.

**Resumability.** Each phase's artifact is the resumption point. If
work is interrupted, the next Claude Code session reads the latest
artifact and resumes from the corresponding phase without redoing
prior work.

## Consequences

- **Positive:** the agent stops at predictable, named checkpoints.
  The architect can audit progress without reading the whole change.
- **Positive:** verification becomes a first-class step, not an
  afterthought of implementation. Bugs that survive `/speckit-implement`
  but fail at `/verify` are caught before they reach a reviewer.
- **Positive:** the PR body's mandatory sections (ADR-0031) are
  populated by phases 5 and 6, not improvised at the end.
- **Negative:** more ceremony than a single-shot implement-and-PR
  workflow. Justified by the operational stakes and by the fact that
  agents need explicit checkpoints to avoid scope creep.
- **Negative:** for very small changes, phases 5 and 6 add overhead.
  Mitigated by the explicit "skipped — reason" mechanism.

## Alternatives Considered

- **Stay with vanilla Spec-Kit**: rejected — testing and review
  collapse into `/speckit-implement` and become invisible to the
  reviewer.
- **More phases (design review, perf review, deploy)**: tempting but
  over-engineered for v1. Add when a recurring failure mode demands
  it.
- **Fewer phases (collapse plan + tasks, or verify + QA)**: rejected.
  Each of these answers a different question; collapsing them loses
  the observable artifact.

## Implementation Notes

- `CLAUDE.md` documents the seven phases as the default workflow and
  instructs Claude Code to stop at each gate.
- `CONTRIBUTING.md` includes the phase table and the skip mechanism.
- The PR template ([ADR-0031](0031-pr-template.md)) already includes
  Test plan and (effectively) verification slots — no template change
  required.
- Cross-referenced from [ADR-0036](0036-karpathy-coding-guidelines.md)
  (Karpathy applies during phases 4–6).
