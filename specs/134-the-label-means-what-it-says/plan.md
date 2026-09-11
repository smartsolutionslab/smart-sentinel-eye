# Plan 134 — The label means what it says

## Constitution / ADR check

| Concern | Verdict |
|---|---|
| ADR-0144 (lane semantics) | Unchanged. This makes the command match the ADR; the ADR already says the label is the enrolment signal. |
| ADR-0144 (eligibility) | Untouched. Todo + `agent:ready` − `agent:blocked` stays exactly as written. |
| ADR-0037 (gates) | No gate weakened, added or moved. Phases 1–3 produce this spec; 4a is declared below. |
| ADR-0036 (smallest change) | One line in each of two command files, plus a data pass. No refactor rides along. |
| Constitution §IV (latency) | N/A — no runtime code. |
| §Testing | Behaviour-changing; red observed on the system, not in the suite. Rationale in spec.md. |

**No ADR is needed.** This is the lane implementing a decision already made, not
making one — precisely the line ADR-0144 §"What the lane may not do" draws.

## The change, in two halves

### Half one — the command files

`next-issue.md` §7, the **Green** bullet. It currently reads: merge → confirm the
issue closed → move the card to Done → remove the worktree and `git branch -D`.
The removal joins that sequence at the point the issue is known closed, using the
same `gh issue edit` idiom the blocked path already uses — so a reader meets one
spelling, not two.

`deliver-board.md` §"Parked PRs" carries a one-line restatement of the same
ending: *"Green → merge, close, card to Done, remove the worktree and
`git branch -D` the local branch."* It gains the same step. This is the file's
own summary of `/next-issue`'s ending, so leaving it out would put the two files
in disagreement — the drift this spec exists to correct, reproduced one file over.

Its line 103 (*"`agent:ready` is the only thing that puts work into it"*) is
**not** touched: it describes entry, it is still true, and it is the sentence the
issue points at only to say it is the file's sole mention.

### Half two — the data

Enumerate first, act from the saved list, so the pass is auditable and
re-runnable:

```sh
gh issue list --label agent:ready --state closed --limit 500 --json number \
  -q '.[].number' | sort -n > closed-with-label.txt
while read -r n; do gh issue edit "$n" --remove-label agent:ready; done < closed-with-label.txt
```

The list is committed under the spec directory as the audit record of what was
changed. `gh issue edit` is **core API**, not Projects v2 — the budget this
session has already exhausted once is not the one this touches, and no board
query is issued. If a limit is hit anyway: stop, record the last number reached,
report it; do not retry blindly.

`--remove-label agent:ready` and nothing else on the command line, so
`agent:blocked` cannot be caught by accident.

## Risks

- **Rate limit.** Core, ~54 writes. Mitigated by acting from a file: a partial
  run resumes by re-measuring, and re-running over an already-clean issue is a
  no-op (verified idempotent).
- **An issue reopened later.** It would then be open and unlabelled — correct:
  re-enrolling it is a human decision, which is exactly what ADR-0144 says the
  label is.
- **The count moving while the work runs.** Re-measured immediately before the
  pass and immediately after; both figures quoted in `verification.md`.

## Verification (phase 5)

Re-run both counts, quote them verbatim, and read back §7 of the edited file to
show where the line sits on the green path.
