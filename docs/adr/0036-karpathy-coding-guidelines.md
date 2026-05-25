# ADR-0036: Adopt Karpathy Guidelines as Baseline Coding Behavior

**Status:** Accepted
**Date:** 2026-05-25

## Context

Smart Sentinel Eye will be co-developed by humans and LLM-driven
agents. LLMs are productive but fall into recurring traps:
over-engineering for hypothetical futures, drive-by refactors that
expand a bug fix into a redesign, silently inventing requirements,
adding "just-in-case" error handling, and finishing tasks without a
clear definition of done.

The Karpathy guidelines (available as the
`andrej-karpathy-skills:karpathy-guidelines` skill) codify behavioural
rules that reduce these mistakes. They align tightly with the
constitution's house rules (§III isolation, §VII observability, §IX
forward-compat) and the latency-budget discipline (§IV). Treating them
as a baseline avoids re-litigating the same review feedback on every
LLM-authored change.

## Decision

Adopt the **Karpathy guidelines** as **baseline coding behaviour** for
this repository. They apply to all code-writing, code-review, and
refactoring work — human or LLM — and are invoked automatically by
Claude Code through the `andrej-karpathy-skills:karpathy-guidelines`
skill when the work falls within its trigger ("writing, reviewing, or
refactoring code").

Concretely, the following behaviours are non-negotiable for changes
in this repository:

- **Smallest possible change.** A bug fix changes the bug, nothing
  else. A refactor changes shape, not behaviour. Mixing the two in
  one PR is a review blocker.
- **Verifiable success criteria up front.** Before any non-trivial
  change, state how we will know it worked — a passing test, a
  measurement, an observable behaviour. No "done when it compiles".
- **Surface assumptions, do not bury them.** When the task is
  ambiguous, ask. Default to one or two clarifying questions before
  guessing. When a guess is unavoidable, mark it explicitly in the
  PR body.
- **No speculative generality.** No frameworks, abstractions,
  configuration knobs, or hooks for needs that do not exist yet. The
  forward-compat strategy interfaces listed in constitution §IX are
  the explicit, scoped exception.
- **No drive-by error handling.** Validate at trust boundaries
  (constitution §VIII); inside trusted code, trust the invariants of
  upstream callers. `try/catch` blocks that swallow exceptions are
  review blockers.
- **No drive-by comments.** Code says what; comments say why, and
  only when the why is non-obvious. References to tasks ("for the X
  flow", "added for issue #42") belong in the PR body, not in code.
- **Read before write.** Before editing a file or function, read the
  surrounding code and tests. Mirror existing patterns rather than
  introducing a new one without justification.

These reinforce, do not replace, the constitution. When in doubt,
the constitution wins.

## Consequences

- **Positive:** review feedback shifts from style/scope debates to
  substantive design discussion.
- **Positive:** LLM-authored PRs need less correction. Predictability
  improves.
- **Positive:** post-mortems become easier because the change set in
  each PR is small and focused.
- **Negative:** initial velocity may feel slower when the agent
  refuses to "fix it while we're here". Net velocity over the
  project is higher because rework drops.
- **Negative:** the guidelines are advisory — they can be violated
  by a determined contributor. Mitigation: PR template review
  checklist and `/code-review` skill in phase 6 of ADR-0037.

## Alternatives Considered

- **No formal adoption** (rely on case-by-case review): considered
  and rejected. The same feedback loops repeat without a written
  baseline.
- **Bespoke project guidelines**: writing our own variant requires
  ongoing maintenance and would diverge from upstream improvements.
  Karpathy's guidelines are well-known, externally maintained, and
  general — adopt them and extend only as needed.
- **Stricter "no comments, no early returns, no exceptions" dogma**:
  rejected as overreach. Karpathy's guidelines are calibrated; ours
  shouldn't be stricter than necessary.

## Implementation Notes

- `CLAUDE.md` adds a "Coding behavior" section pointing at this ADR
  and naming the skill.
- `CONTRIBUTING.md` adds a paragraph in the workflow section noting
  that the skill is invoked automatically.
- Cross-referenced from [ADR-0037](0037-guided-phased-development.md)
  (Karpathy applies during phases 4–6).
