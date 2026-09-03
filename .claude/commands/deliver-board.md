---
description: Run the autonomous lane over the board — deliver every agent:ready issue, one at a time, until none remain (ADR-0144).
argument-hint: "[max-issues]  (default: until the board is empty)"
---

# Deliver the board

Repeat `/next-issue` until no eligible issue remains. `$ARGUMENTS`, if
given, caps how many issues to deliver this run.

## The loop

```
while an eligible issue exists (and the cap is not reached):
    run /next-issue
    record one line: issue, outcome, PR or block reason
    forget everything else about that issue
```

Eligibility, claiming, the phases, the merge and the blocked exit all
live in `/next-issue`. Do not restate or reinterpret them here.

## What you must not accumulate

You are the outermost loop, so you are the context that grows. Between
issues, carry **one line per issue** — number, outcome, link — and
nothing else. No spec text, no diffs, no test output, no file contents.
If you find yourself recalling issue N while working issue N+2, you have
already failed the thing this command exists to do.

The board is the memory. Todo is available, In Progress is claimed, Done
is merged. That is enough to resume this loop from a cold start, so it
is enough to run it.

## Stopping

Stop, and say which of these happened:

- **No eligible issue.** The expected ending. Report the tally.
- **The cap was reached.**
- **Three consecutive issues blocked.** Something is wrong with the
  environment, not with three unrelated issues — a broken `develop`, a
  down stack, expired credentials, an exhausted rate limit. Stop and say
  what the three failures had in common.
- **Projects v2 rate limit exhausted.** It has its own budget, separate
  from core. Report where the loop got to; do not poll it.

Never widen eligibility to keep going. An empty lane is the correct
result of an empty lane, and `agent:ready` is the only thing that puts
work into it.

## Report

A table — issue, title, outcome, PR — then the tally: delivered,
blocked, skipped. Then a single line naming what a human should look at
first.
