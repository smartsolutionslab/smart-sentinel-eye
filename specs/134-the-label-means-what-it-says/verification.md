# Verification 134 — The label means what it says

**Phase 5, ADR-0037.** Observed against the real repository, not against a test
double. Every figure below is a command run on this branch, quoted verbatim.

## The invariant, before

`agent:ready` claims an issue is enrolled in the autonomous lane (ADR-0144). The
invariant that makes it a true statement is: **no closed issue carries it.**

```
$ gh issue list --label "agent:ready" --state closed --limit 500 --json number -q '.[].number' | wc -l
54
$ gh issue list --label "agent:ready" --state open   --limit 500 --json number -q '.[].number' | wc -l
29
$ gh issue list --label "agent:ready" --label "agent:blocked" --state closed --limit 500 --json number -q '.[].number' | wc -l
0
```

**Red: the invariant was false — 54, where it must be 0.** The label answered 83
to "how many issues are enrolled in the lane" when the answer was 29.

The issue was filed at 30 closed. Twenty-four of the twenty-four-issue increase
came from the delivering session's own merges, each one correctly following
`next-issue.md` as it was written. The enumeration is committed as
`closed-with-label.txt` (54 numbers, #91 … #2219) — the audit record of what was
about to be changed, saved before anything was.

No closed issue carried `agent:blocked`, so the pass could not touch it even by
accident. It was also given nothing to touch: the cleanup passes
`--remove-label agent:ready` and no other label.

## The double-removal question, settled empirically

If the green path removes the label, the blocked path's existing
`--remove-label agent:ready` could later run against an issue that no longer
carries it. Checked against the API rather than reasoned about, on #91:

```
$ gh issue edit 91 --remove-label agent:ready      # label present
https://github.com/smartsolutionslab/smart-sentinel-eye/issues/91
exit=0
$ gh issue edit 91 --remove-label agent:ready      # label now absent
https://github.com/smartsolutionslab/smart-sentinel-eye/issues/91
exit=0
```

**Idempotent.** No guard added, and the blocked path is left exactly as it was.
(#91 is therefore the one issue of the 54 cleared by the probe rather than by the
pass; the pass ran over the remaining 53.)

## The pass

Acted from the saved list, one `gh issue edit` per line, each outcome recorded:

```
$ while read -r n; do gh issue edit "$n" --remove-label agent:ready; done < closed-now.txt
ok=53 fail=0
```

`cleanup-log.txt` holds one line per issue. **53 changed, 0 failed**, plus #91
from the probe: **54 of 54**. No rate limit was reached — these are core-API
writes, and no Projects v2 query was issued at all.

## The invariant, after

```
$ gh issue list --label "agent:ready" --state closed --limit 500 --json number -q '.[].number' | wc -l
0
$ gh issue list --label "agent:ready" --state open   --limit 500 --json number -q '.[].number' | wc -l
29
$ gh issue list --label "agent:blocked" --state closed --limit 500 --json number -q '.[].number' | wc -l
0
```

**Green: 54 → 0.** The open count is **unchanged at 29** — the pass touched
closed issues only, and no open issue lost its enrolment. `agent:blocked` on
closed issues is still 0: untouched, as scoped out.

#2176 itself is still `OPEN tech-debt,agent:ready` — correct. It is in flight,
and the line added by this change is what will take the label off it at merge.

## Where the line now sits — the green path walked

`.claude/commands/next-issue.md` §7, the **Green** bullet, in order: merge →
confirm the issue actually closed → card to **Done** (`98236657`) →

```sh
gh issue edit <N> --remove-label agent:ready
```

→ remove the worktree → `git branch -D`. The removal sits **after** the close is
confirmed, so an issue whose merge did not close it keeps its label and stays
visible. The idiom is the one the blocked path already uses, so a reader meets
one spelling of label editing, not two.

`.claude/commands/deliver-board.md`'s one-line restatement of the same ending now
reads "*Green → merge, close, card to Done, **`agent:ready` off the issue**,
remove the worktree and `git branch -D`*" — the two files agree. Its line 103
("`agent:ready` is the only thing that puts work into it") is untouched: it is
about entry, and it is still true.

## What was not verified, and cannot be here

**That a future lane run actually executes the new line.** These are command
files an agent reads; nothing in the repository executes them, and no test can
observe the lane obeying prose. The next merge through `/next-issue` is the first
real observation, and the standing check is the one-line invariant above — if it
ever reads non-zero again, the step is being skipped.

That is also why no guard was added asserting the markdown contains the string:
it would prove the design was written down, not that it holds.

## Selection — unchanged, and confirmed unchanged

```
$ git diff origin/develop -- .claude/commands/next-issue.md | grep -c 'status=="Todo"'
0
```

The eligibility query at `next-issue.md:35` is not in the diff. ADR-0144's rule —
Todo, plus `agent:ready`, minus `agent:blocked` — is exactly as it was. This
change fixes a **true statement**, not a bug in selection; the board status join
is why no closed issue was ever picked up in error.

## Latency

**N/A** — no runtime code, no service, nothing on the event-to-overlay path
(constitution §IV).
