# ADR-0144: An autonomous delivery lane

**Status:** **Accepted**
**Date:** 2026-09-03
**Amends:** ADR-0037 — the gate rule, for opted-in issues only

**Supersedes:** —
**Superseded by:** —

## Context

ADR-0037 defines seven phases and says, in as many words, that **Claude
Code does not autonomously advance past a gate**. Every phase hands its
artifact back and waits for a human.

That rule was written for a session a human is watching. It is the right
rule there, and it stays. But it makes one thing impossible: leaving the
agent to work through the board. A loop that stops seven times per issue
is not a loop — it is the same supervised session with extra ceremony.

The board is where this bites. Project #13 carries the in-flight work at
feature granularity, and its Todo lane holds ready, well-described,
independently-shippable issues — an unmeasured latency leg, a test that
covers nothing, a default that costs throughput. Each is a day's work
that needs a human for about four minutes of it: deciding it is worth
doing, and deciding the result may merge. The first decision was already
made when the issue was written. The second is already delegated — green
CI plus `--rebase --admin` is the standing merge convention here.

So the ceremony buys nothing on these issues, and costs the only thing
that would make the board drain: unattended runs.

Two further pressures shape the decision.

**Context is the scarce resource, not tokens.** An issue carried
end-to-end in one session accumulates the spec, the plan, every file
read during implementation, every test run, and the review. By phase 6
the session is reasoning about phase 4 through a summary of a summary.
The phases are already role-shaped — ADR-0037 names a different question
for each — and `.claude/agents/` already holds eight role definitions
that nothing currently binds to a phase.

**Red-first is the phase that autonomy most easily eats.** ADR-0139
requires a new-behaviour test to be *observed failing* and the failure
quoted in the PR. An unattended agent that writes test and implementation
together will produce a green test and a truthful-sounding claim that it
was red. The lane has to make the red output a transported artifact, not
a recollection.

## Decision

Add a second lane. ADR-0037's seven phases, artifacts and role
boundaries are unchanged; what changes is who opens the gate, and only
for issues explicitly enrolled.

### Two lanes

**The supervised lane is the default and is unchanged.** Any work a
human starts — a request in a session, a spec they asked for — runs
ADR-0037 exactly as written, stopping at all seven gates.

**The autonomous lane** runs phases 1–6 without stopping and merges at
phase 7 on green CI. An issue enters it only by carrying the
**`agent:ready`** label. Nothing else is eligible; absence of the label
is a hold, so a newly-filed issue is never picked up by accident.

### Eligibility

An issue is eligible when **all** of the following hold:

- it is on Project #13 with status **Todo**;
- it carries **`agent:ready`**;
- it does not carry **`agent:blocked`**.

Ties break **oldest issue number first**. Applying `agent:ready` is the
human gate for phases 1–6, made once, in advance, in writing.

### Phases and their roles

Each phase runs in a **subagent with its own context**. The orchestrator
holds only the issue, the branch name, and each phase's report — never
the files those phases read. This is what makes an unattended run
survive a whole issue and then start the next one clean.

| # | Phase | Agent | Carries forward |
|---|---|---|---|
| 1 | Specify | `architect` | `specs/NNN-x/spec.md` |
| 2 | Plan | `architect` | `plan.md` |
| 3 | Tasks | `architect` | `tasks.md` |
| 4a | Red tests | `test-writer` (+ `test-adversary` where the issue is about a failure mode) | **the failing test output, verbatim** |
| 4b | Implement | `backend-engineer` / `frontend-engineer` / `infra-engineer` | commits, green test output |
| 5 | Verify | the same engineer, or the orchestrator | observed behaviour; latency figure if on the §IV path |
| 6 | Review | `backend-reviewer` / `frontend-reviewer` / `infra-reviewer`, plus `security-reviewer` where the change touches a trust boundary, then `/code-review` | findings, each fixed or refused in writing |
| 7 | PR + merge | orchestrator | the PR, then the merge |

Phase 4 is **two agents, in order, and the split is the point**. The
test-writer is told the intended behaviour and is forbidden from
touching implementation code. Its report must contain the actual failing
output. The engineer then receives that output as its brief and may not
edit the test to make it pass. A test file arriving already green is a
phase-4 failure, not a shortcut — the run is retried, once, from 4a.

The verbatim red output is quoted in the PR body, which is what ADR-0139
asks for and what a later reader can check.

### Merging

The orchestrator opens the PR against `develop` with `--base develop`,
waits for CI, and merges with `--rebase --admin --delete-branch` when
**every** check has concluded successfully. A cancelled, skipped-required
or failed check is not green; `gh run watch` exiting is not evidence of
green (it exits on cancellation too), so the merge condition is read from
the check conclusions, not from a watcher's exit.

Review happens **after** the merge, on `develop`'s history. That is the
trade this lane makes and the reason eligibility is opt-in.

### Failure

A phase that fails is retried **once**, in a fresh subagent, with the
failure text as part of its brief. If it fails again the orchestrator:

1. comments on the issue with the phase, the retry, and the verbatim
   failure;
2. applies **`agent:blocked`** and removes `agent:ready`;
3. returns the board card to **Todo**;
4. leaves the branch and any open PR untouched, for a human;
5. **continues to the next eligible issue.**

One bad issue must not end an overnight run. An issue that fails twice
has earned a human, and says so on itself.

### What the lane may not do

- **It may not amend the constitution or write an ADR autonomously.** An
  issue whose honest answer is a new architectural decision is blocked
  with that as the reason. This is the boundary the loop must not cross:
  it implements decisions, it does not make them.
- **It may not weaken a gate to pass.** Deleting a failing test, relaxing
  a coverage threshold, adding a suppression, or narrowing an analyzer to
  get green is a blocked outcome, not a fix.
- **It may not skip phase 4a.** Every other phase has ADR-0037's skip
  mechanism for trivial changes; red-first does not, because a change too
  trivial to test is too trivial to need this lane.

## Consequences

- **Positive:** the board can drain unattended. The two decisions that
  are expensive to get wrong stay human — what to build (`agent:ready`)
  and what the architecture is (blocked) — and the five that are cheap
  stop costing a round trip each.
- **Positive:** per-phase subagents mean phase 6 reviews the code rather
  than a summary of it, and issue N+1 starts with a context that has
  never seen issue N.
- **Positive:** red-first stops being a claim and becomes a transported
  artifact. The engineer cannot produce the red output it was handed.
- **Negative:** review moves after the merge. A defect that survives
  phase 6 lands on `develop` and is found by the next reader, not before.
  Bounded by opt-in eligibility and by rebase-only history, which keeps
  a bad commit individually revertable.
- **Negative:** two lanes is two rules, and the wrong one can be applied.
  Mitigated by making the distinction a label rather than a judgement.
- **Negative:** an unattended run can spend a long time on an issue that
  a human would have abandoned in a minute. Bounded by the one-retry
  policy, not eliminated by it.

## Alternatives Considered

**Keep all seven gates, make them asynchronous** — post the artifact,
label `gate:awaiting-<phase>`, park the issue, move on. Rejected: it
preserves every gate and delivers nothing, because the board fills with
half-built issues whose branches drift behind `develop` while they wait.
Seven human touches per issue is the cost being removed, and spreading
them over a week does not remove it.

**Gate at spec and PR only** — the loop stops twice per issue. Genuinely
attractive, and rejected only because the spec gate duplicates a decision
already made: an issue on the board carrying `agent:ready` has been read
and judged worth doing. A second approval of the agent's reading of it
buys less than it costs in stopping the loop.

**Full autonomy for everything in Todo** — no label. Rejected: the Todo
lane today contains investigations phrased as issues ("nine of the
twenty-seven founding decisions do not describe the system that exists"),
whose honest first output is an ADR. The lane may not write one, so it
would block immediately and repeatedly.

**Opt-out (`agent:hold`) rather than opt-in.** Rejected for the same
reason, plus one worse: a newly-filed issue would be eligible the moment
it lands, including one filed to record a problem rather than to request
a fix.

**A headless driver script (`claude -p` per issue).** This gives a
genuinely new process per issue and is the strictest reading of "fresh
context". Not adopted now: per-phase subagents already give each phase a
clean context, and the orchestrator's own state is small and lives on the
issue and the board rather than in its head — so it can be resumed from
scratch. Revisit if orchestrator context turns out to be the binding
constraint over a long run.

## Implementation Notes

- `/next-issue` delivers one eligible issue end-to-end. `/deliver-board`
  repeats it until none remain.
- `/verify` implements ADR-0037's phase 5, which CLAUDE.md and ADR-0037
  have both referenced since 2026-05-25 without it existing.
- Labels `agent:ready` and `agent:blocked` are created in the repository.
- Board state is the run's memory: **Todo** is available, **In Progress**
  is claimed, **Done** is merged. An orchestrator restarted mid-issue
  reads the card and the branch, not a transcript.
- `.claude/agents/backend-engineer.md` and `backend-reviewer.md` both
  said "NRT disabled" long after ADR-0141 enabled it. Corrected as part
  of this work: a subagent brief is now load-bearing, and a stale one
  writes stale code without a human present to catch it.
- **Two reviewer roles are added**, because phase 6 previously had
  reviewers for backend and frontend only:
  - **`infra-reviewer`** — the layer every integration test stands on
    had no reviewer at all. An infra defect does not fail like a code
    defect; it fails as everything failing, or as everything passing for
    the wrong reason.
  - **`security-reviewer`** — `/security-review` is a skill with no
    repo-specific brief, so it cannot tell a correct `RequireScope` from
    a plausible-looking wrong one, and it does not know that
    `sse.management` grandfathers every granular policy or that an
    idempotency key scoped without the caller is a cross-tenant leak.
    The skill still runs; this role is what reads the authorization
    model.
