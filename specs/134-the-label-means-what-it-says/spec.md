# Spec 134 — The label means what it says

**Issue:** #2176 — *The lane clears `agent:ready` when it fails and leaves it when it succeeds*
**Branch:** `chore/2176-the-label-means-what-it-says`
**ADRs:** 0144 (the autonomous lane; `agent:ready` is its eligibility signal),
0037 (phases and gates), 0036 (smallest change), 0086 (no `Co-Authored-By`), 0139 (red first)

## The defect, restated

`.claude/commands/next-issue.md` removes `agent:ready` on **exactly one path** —
the one where the lane gives up (§Blocked, line 282):

```sh
gh issue edit <N> --add-label agent:blocked --remove-label agent:ready
```

§7 "PR and merge" and its green bullet do three things on success — merge,
confirm the issue closed, move the board card to **Done** — and none of them
touches the label. So the label is cleared when the lane **fails** and left
standing when it **succeeds**, which is backwards from what any reader would
guess from ADR-0144's "applying `agent:ready` is the human gate for phases 1–6".

## What is wrong is a statement, not a selection

This is worth stating precisely, because the two are easy to conflate and the
fix for one is not the fix for the other.

**Selection is correct and is not being changed.** `next-issue.md:35` filters on
`.status=="Todo"` *as well as* the label, so a closed, delivered issue has never
been picked up in error and cannot be. ADR-0144's eligibility rule — Todo, plus
`agent:ready`, minus `agent:blocked` — stays exactly as written.

**The statement is false.** `agent:ready` is defined by ADR-0144 as "this issue
is enrolled in the lane". On 54 closed issues it asserts that about work that is
finished and merged. Anything reasoning from the label alone — a report, a human
running `gh issue list --label agent:ready`, a future selector that does not
happen to join against the board — is told 83 when the answer is 29. The board
status is the only thing standing between that label and a wrong answer, and
nothing anywhere records that the label alone is unreliable.

So this spec fixes a **true statement**. It does not fix a bug in selection,
because there is no bug in selection.

## The count has moved since the issue was filed, and the lane moved it

The issue records 30 closed and 43 open. Measured on this branch before any
change:

```
$ gh issue list --label "agent:ready" --state closed --limit 500 --json number -q '.[].number' | wc -l
54
$ gh issue list --label "agent:ready" --state open   --limit 500 --json number -q '.[].number' | wc -l
29
```

**Twenty-four of the twenty-four-issue increase came from the session that is
now fixing it.** The lane produced the overwhelming majority of its own drift,
one merge at a time, faithfully following the command file as written. That is
the argument for putting the removal in the command rather than doing a cleanup
pass: a cleanup alone would be re-earned within a day.

It is the same class of defect this repository keeps correcting — a recorded
signal nobody checks against what is actually happening (CLAUDE.md §Phase 3,
constitution §IV, ADR-0048).

## Scope

**In:**

1. `.claude/commands/next-issue.md` §7's green path removes `agent:ready`
   alongside the existing close-confirm and card-to-Done steps, using the
   `gh issue edit` idiom already in the file.
2. The closed issues carrying the label are cleaned up in one enumerated,
   re-runnable pass, from a saved list.
3. `deliver-board.md`'s one-line restatement of the green ending ("Green →
   merge, close, card to Done, …") gains the same step, so the two files do not
   disagree. Its line 103 — "`agent:ready` is the only thing that puts work into
   it" — stays as it is; it is still true and it is about entry, not exit.

**Out:**

- **`agent:blocked` is untouched.** A closed issue carrying it is a different
  question (a blocked issue that was later closed by hand) and not this one. No
  closed issue carries both today — measured: 0.
- **The selection query is untouched** (see above).
- **No ADR.** ADR-0144 already says what the label means; this makes the command
  match it. No gate is weakened, added or moved.

## Done looks like

- **The invariant holds:** `gh issue list --label agent:ready --state closed`
  returns **0**, where it returned 54.
- The open count is **unchanged at 29** — the cleanup touches closed issues only.
- `next-issue.md` §7's green bullet names the label removal; `deliver-board.md`'s
  green summary says the same.
- The blocked path is unaffected and cannot error on a second removal.

## The blocked path cannot double-remove — verified, not assumed

If the green path now removes the label, a later `--remove-label agent:ready` in
the blocked path could in principle run against an issue that no longer carries
it. Checked against the real API rather than reasoned about:

```
$ gh issue edit 91 --remove-label agent:ready      # label present
https://github.com/smartsolutionslab/smart-sentinel-eye/issues/91
exit=0
$ gh issue edit 91 --remove-label agent:ready      # label now absent
https://github.com/smartsolutionslab/smart-sentinel-eye/issues/91
exit=0
```

`gh issue edit --remove-label` is **idempotent**: removing an absent label exits
0 and prints the issue URL. No guard is needed, and none is added. (In practice
the two paths are mutually exclusive anyway — the blocked exit is only reached
when no merge happened — but the cleanup pass has already unlabelled issues a
human could reopen, so the property is worth having established.)

## Phase 4a — declared colour, and why there is no honest red in the suite

Declared at phase 3: **behaviour-changing**, and the honest red is a **measured
observation of the real system**, not a test in the suite.

The change has two halves and neither admits a suite test that could fail for
the right reason:

- **The command file is prose an LLM executes.** A test asserting that
  `next-issue.md` contains the string `--remove-label agent:ready` in §7 would go
  red before and green after — but it would prove the design was *written down*,
  not that the lane behaves. This repository has that failure mode on record, and
  nine assertions that could not meaningfully fail surfaced in this session
  alone. Adding a tenth to satisfy a ritual is worse than not having one.
- **The cleanup is data in GitHub.** The invariant "no closed issue carries
  `agent:ready`" is genuinely checkable — but only against mutable external state
  over the network, with board credentials CI does not have. In the suite it
  would be flaky, not a guard.

So the red is taken where it is real: the invariant was **observed false (54)**
before the change and **true (0)** after, both quoted verbatim in
`verification.md`, alongside the unchanged open count. That is a red-first
observation of the actual system, which is the thing ADR-0139 is protecting; a
string-match on markdown is not.

## Latency

**N/A** — no runtime code, no service, no path to an overlay. Nothing in this
change is reachable from the event-to-overlay budget (constitution §IV).
