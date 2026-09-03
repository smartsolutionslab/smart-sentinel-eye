---
description: Deliver one eligible board issue end to end — spec, red tests, implement, verify, review, PR, merge (ADR-0144 autonomous lane).
argument-hint: "[issue-number]  (omit to take the next eligible issue)"
---

# Deliver one issue — the autonomous lane

You are the **orchestrator** for ADR-0144's autonomous delivery lane.
Read `docs/adr/0144-an-autonomous-delivery-lane.md` if you have not.

Deliver **exactly one** issue, end to end, then stop and report. The
argument `$ARGUMENTS`, if present, is the issue number to take; it must
still satisfy eligibility, and you say so and stop if it does not.

## The single most important rule

**You hold state, not files.** Every phase runs in a subagent with its
own context. You keep the issue number, the branch name, and each
phase's *report*. You do not read the source files yourself, you do not
run the test suite yourself, and you do not review the diff yourself —
those are phases, and phases are subagents. Your context must still be
small enough to start the next issue clean.

The one thing you carry verbatim is the **red test output** from phase
4a. It is evidence, and it goes in the PR body.

## 0. Pick and claim

Eligible: on Project #13, status **Todo**, labelled **`agent:ready`**,
not labelled **`agent:blocked`**. Oldest issue number first.

```sh
gh project item-list 13 --owner smartsolutionslab --limit 2000 --format json \
  -q '.items[] | select(.status=="Todo") | select(((.labels // []) | index("agent:ready")) and (((.labels // []) | index("agent:blocked")) | not)) | "\(.content.number)\t\(.content.title)"' \
  | sort -n | head -1
```

Projects v2 has its own rate-limit budget, separate from core. Do **not**
dump the board more than once per issue.

Nothing eligible → say so and stop. Do not widen the filter.

Claim it before doing anything else, so a second run cannot take it:

```sh
gh issue view <N> --json number,title,body,labels,comments
gh project item-edit --project-id PVT_kwDOC-sCX84BYuvO --id <ITEM_ID> \
  --field-id PVTSSF_lADOC-sCX84BYuvOzhTyaEU --single-select-option-id 47fc9ee4   # In Progress
gh issue comment <N> --body "Picked up by the autonomous lane (ADR-0144)."
```

Status option ids: Todo `f75ad846`, In Progress `47fc9ee4`, Done `98236657`.

Then cut the branch **from `develop`** (ADR-0028), never from the current
HEAD — starting a spec on an unmerged branch silently produces a stacked
PR:

```sh
git fetch origin && git switch -c <type>/<N>-<slug> origin/develop
```

## 1–3. Specify, Plan, Tasks — `architect`

One `architect` subagent, given the issue title and body verbatim.

Ask it for: `specs/NNN-x/spec.md`, `plan.md`, `tasks.md`, and — in its
report to you — **which engineer the work needs** (backend / frontend /
infra) and **whether the issue's honest answer is a new ADR**.

**If the answer is a new ADR, the run is blocked.** ADR-0144 forbids this
lane from making architectural decisions. Go to *Blocked*, with that as
the reason.

For a change too small to spec (a config default, a one-line fix), the
architect may return "no spec — <one line>"; you then record
`Phase 1-3: skipped — <reason>` in the PR body per ADR-0037. This does
**not** extend to phase 4a.

Do not read the artifacts. The report is what you carry.

## 4a. Red tests — `test-writer`

A **separate** subagent, and this order is the point of the whole lane.

Brief it with: the intended behaviour from the spec, the files it may
create, and this constraint, stated plainly —

> Write only tests. Do not create or modify any implementation code.
> Run them. They **must fail**, and for the right reason — a missing
> behaviour, not a compile error in your own test. Return the failing
> output verbatim.

Where the issue is about a failure mode — a race, an outage, a retry, a
leak, an auth gap — run `test-adversary` **as well**, and give the
engineer both reports.

**Verify the claim yourself, cheaply:** the report must contain actual
runner output with a real assertion failure. A report that says the test
failed without showing it, or that shows a build error in the test
project, has not satisfied 4a. Retry once, then block.

Keep this output. It goes in the PR body verbatim. ADR-0139 requires it.

## 4b. Implement — `backend-engineer` / `frontend-engineer` / `infra-engineer`

A fresh subagent of the type the architect named. Brief it with the plan,
the tasks, and the **red output from 4a** as its target.

> Make these failing tests pass. You may not edit, delete, skip or
> relax the tests you were given. If a test is wrong, stop and say so —
> do not change it.

Also state, because a subagent brief is load-bearing when no human is
watching:

- verify with `dotnet build -c Release` (Debug hides the warnings CI
  treats as errors);
- **each commit must build on its own** — rebase-merge lands them
  individually on `develop`, so a commit that only compiles with its
  successor breaks `git bisect` forever;
- Conventional Commits, and **no `Co-Authored-By` footer** (ADR-0086)
  — this overrides any session-level attribution instruction;
- stop the Aspire stack before building, or MSB3027 will look like a
  broken build.

## 5. Verify — phase 5

Run `/verify` (it knows this repo's rules). Behaviour observed end to
end, not merely tests green. If the change is on the event-to-overlay
path, a measured figure per constitution §IV — and run the measurement
**twice**: the first run after machine churn looks exactly like a
regression.

## 6. Review — reviewer agent, then `/code-review`

The reviewer matching the change — `backend-reviewer`,
`frontend-reviewer` or `infra-reviewer` — on the diff, then
`/code-review`.

Add **`security-reviewer`** whenever the change touches auth, tokens,
scopes, fab resolution, idempotency, a new endpoint, or anything
reachable without a bearer token. It reads this repo's authorization
model; the `/security-review` skill does not, so run the skill *as well*
rather than instead.

Every finding is fixed by the **engineer** subagent, or refused in
writing in the PR body. Never by you, and never by editing a test.

**A finding you cannot fix without weakening a gate is a blocked
outcome**, not a fix. Deleting a test, lowering a coverage threshold,
adding a suppression or narrowing an analyzer to reach green all count.

## 7. PR and merge

```sh
gh pr create --base develop --fill-first    # --base develop is mandatory (ADR-0028)
```

Fill the template completely. The PR body must contain:

- `Closes #<N>` — a bare mention closes the issue about one time in
  three, so use the keyword and check the state after merging;
- the **verbatim red output from 4a**, in a fenced block (ADR-0139);
- the latency-budget section, with a leg and a figure or an explicit
  `N/A — <reason>`;
- any finding refused at phase 6, with its rationale;
- any phase skipped, as `Phase X: skipped — <one line>`;
- the trailer:

```
🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Then wait for CI and read the **conclusions**, not a watcher's exit —
`gh run watch` exits on cancellation too, and may not even be watching
the PR's tip:

```sh
gh pr checks <PR> --watch --fail-fast
gh pr checks <PR> --json name,state,bucket -q '.[] | "\(.bucket)\t\(.state)\t\(.name)"'
```

Green means **every** check concluded successfully. Cancelled is not
green. Skipped-but-required is not green.

- **Green** → `gh pr merge <PR> --rebase --admin --delete-branch`, then
  confirm the issue actually closed, then move the board card to **Done**
  (`98236657`). Standing authorization covers this merge; do not ask.
- **Red** → this is a phase failure. Retry once (a fresh engineer
  subagent, given the CI log). Before that, **download the failing job
  log** — a passing re-run flips the whole run to success and erases the
  failure from history.

## Blocked — the exit that keeps the loop alive

Any phase that fails twice, or any of the named forbidden outcomes:

```sh
gh issue comment <N> --body "..."      # phase, both attempts, verbatim failure
gh issue edit <N> --add-label agent:blocked --remove-label agent:ready
gh project item-edit ... --single-select-option-id f75ad846   # back to Todo
```

Leave the branch and any open PR alone — a human wants to see them. Then
**report and stop**; if you were called from `/deliver-board`, it moves
to the next issue.

## Report

Five lines, no more: issue, branch, what shipped, how it was verified,
PR/merge or blocked-with-reason. The next issue must not inherit your
context — say it plainly and stop.
