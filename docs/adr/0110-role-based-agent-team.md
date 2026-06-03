# ADR-0110 — Role-based agent team

**Status:** Accepted

**Relates to:** ADR-0109 (parallel worktree workflow), ADR-0037 (7-phase workflow + gates), ADR-0036 (Karpathy guidelines), ADR-0052 (test stack), ADR-0108 (e2e gate)

## Context

ADR-0109 established parallel slice development in worktrees, but dispatched a
single general-purpose agent for every slice. Different phases and concerns
demand different expertise and different *posture* — a requirements engineer
thinks differently from a backend implementer, and a constructive tester from
an adversarial one. We want specialized agents so each piece of work gets
role-appropriate judgement, and so review is a distinct, read-only step.

## Decision

Define eight Claude Code subagents under **`.claude/agents/`**, mapped onto the
7-phase workflow (ADR-0037):

| Phase | Agent(s) | Posture |
|---|---|---|
| 1–3 Specify / Plan / Tasks | **architect** | requirements engineer + architect; produces spec/plan/tasks, clarifies, aligns with ADRs, slices for parallelism |
| 4 Implement | **infra-engineer** / **backend-engineer** / **frontend-engineer** | by concern (Aspire+CI / C#+.NET+DB / TS+React+UX) |
| 4–5 Test | **test-writer** + **test-adversary** | one proves it works (happy + standard cases), one tries to break it (edges, races, auth, failure modes) |
| 6 Review | **backend-reviewer** + **frontend-reviewer** | **read-only**; rank findings, never edit |
| 7 PR / integrate | the **orchestrator** (lead session) | dispatches roles, integrates, watches CI, rebase-merges |

- **Implementers + testers** have full tools; they **implement, verify locally,
  and report** — the orchestrator commits/pushes/opens PRs (ADR-0109's lesson:
  subagent push is sandbox-unreliable, and orchestrator integration keeps every
  push gated).
- **Reviewers** are **read-only** (`tools: Glob, Grep, Read, Bash, WebFetch` —
  no Edit/Write); they report a ranked findings list and never fix code.
- **Models are inherited** from the session by default; override per role later
  if a concern warrants it.

The orchestrator composes them: pick the implementer by concern; run
test-writer **and** test-adversary together on a slice; run the matching
reviewer before merge. Each role's file embeds the slice of the
constitution/ADRs it needs.

## Consequences

**Positive:** role-appropriate expertise; an adversarial tester paired with a
constructive one catches more than either alone; two-lens (backend/frontend)
read-only review separates "find problems" from "fix them"; the team is
versioned in-repo, so every contributor's agents behave the same.

**Negative:** eight prompts to keep in sync with the conventions as ADRs evolve
(each cites the rules it depends on, so drift is visible); the orchestrator must
still enforce the disjoint-file rule and the gates — the roles don't self-merge.
