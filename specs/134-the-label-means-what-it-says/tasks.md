# Tasks 134 — The label means what it says

Declared at phase 3: **behaviour-changing**; phase 4a's red is a measured
observation of the real system (spec.md §Phase 4a). Engineer: infra.

- [x] **T1** — Measure and save the before state: closed-with-label,
      open-with-label, and closed-carrying-both. Commit the enumeration as
      `closed-with-label.txt`.
- [x] **T2** — Establish empirically that `gh issue edit --remove-label` is
      idempotent, so the blocked path cannot error after the green path removes
      the label. Quote both invocations.
- [x] **T3** — `next-issue.md` §7 green bullet: remove `agent:ready` alongside
      confirming the issue closed and moving the card to Done.
- [x] **T4** — `deliver-board.md` §"Parked PRs": the same step in its one-line
      restatement of the green ending. Leave line 103 alone.
- [x] **T5** — Run the cleanup pass from the saved list. Report the count
      changed and any failure, by number.
- [x] **T6** — Re-measure; write `verification.md` with before/after verbatim.

Not done, deliberately: no ADR, no change to the selection query, no touching of
`agent:blocked`, no markdown-string guard.
