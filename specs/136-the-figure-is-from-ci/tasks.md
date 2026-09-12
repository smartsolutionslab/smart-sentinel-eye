# Tasks 136 — The figure is from CI

Declared at phase 3: **behaviour-preserving** → phase 4a is **characterisation, observed
green**. Engineer: **none required for judgement** — the work is documentation plus a
comment block. Assign **infra** if one is needed, because the evidence lives in `ci.yml`
artifacts and the reader must be fluent with `gh run download`; no backend or frontend
skill is exercised.

All tasks are US-1. Grouping is by file ownership, so the `[P]` markers are real
disjoint-file parallelism (ADR-0109).

## Foundational — blocks everything

- [X] **T001** [US-1] Re-run the measurement on **every** current green `develop` run in
      a multi-day window — **forty**, not four — and record the figures. Do not reuse
      this spec's table if more than a few days have passed: artifact retention is 14
      days, and the first run after machine churn reads like a regression.
      ```sh
      gh run list --workflow ci.yml --branch develop --status success --limit 40 \
        --json databaseId -q '.[].databaseId' | while read -r R; do
        rm -rf /tmp/ci-run
        gh run download "$R" -n integration-test-results -D /tmp/ci-run || continue
        printf '%s ' "$R"
        grep -rohE 'CONNECT.CONNACK over [^<]{0,160}' /tmp/ci-run | head -1
      done
      ```
      **Done when:** forty lines, each ending `(budgets enforced)`, with p50 and p99
      values, the sample min/max/mean, and the **margin floor** against 15 ms and 50 ms
      written down.
      **Amended at phase 6 (2026-09-12).** This task originally said *four*, and four
      runs produced a p50 range (1.98–2.29 ms) that a later green run — 34586548968,
      p50 2.40 ms, itself inside the `--limit 4` the task printed — falsifies. Four
      runs can refute "has gone red"; a **range** needs the whole window, and the claim
      has to be phrased as a sampled floor rather than a constant.
      Blocks T002–T007.

- [X] **T002** [US-1] Phase 4a characterisation, **green**. Establish the two facts the
      later tasks must preserve:
      (a) the forty T001 lines are the "before" reading of the **unmodified** file;
      (b) `git diff` of `NFR002_MqttConnectAuthTests.cs` restricted to non-comment lines
      is empty at every later point.
      **Done when:** the verbatim forty lines are captured, and the non-comment-diff
      check is written down as the command it is, ready to re-run at T008. The command
      and its hash are in `verification.md`.
      Depends on T001.

## The point of enforcement

- [X] **T003** [US-1] Add a **CI paragraph** to the `<remarks>` of
      `tests/Integration.Tests/Identity/NFR002_MqttConnectAuthTests.cs`, beside spec
      123's off-CI paragraph at `:79-90` — not replacing it. It carries: the sample
      size and window, p50 margin floor 4.22×, p99 margin floor 3.85×, the ≈ 14× (p50)
      / ≈ 15× (p99) environment ratio, a pointer to `verification.md` for the per-run
      ids, and one sentence separating the 50 ms wall-clock ceiling from NFR-002's 5 ms
      auth-overhead SLO (ADR-0100, `specs/008-*/spec.md:425`).
      **Must not touch:** `:51`, `:55`, `:92-93`, `:130-132`, `:138`, `:141` —
      pre-change coordinates; post-change they are `:51`, `:55`, `:123-124`, `:161-164`,
      `:169`, `:172` (see the line-shift table in `spec.md`).
      **Check ADR-0084's 300 LOC/file limit before and after**; if it would breach, the
      figures go to `verification.md` and the remarks keep a two-line summary and a cite.
      Depends on T002. Owns this file exclusively.

## The four records — three parallel lanes, disjoint files

- [X] **T004** [P] [US-1] `specs/021-transactional-outbox/verification.md:212-221`.
      Rewrite the CONNECT→CONNACK sentence so it reads as an off-CI, budgets-not-enforced
      observation. Both "breached" and "passed after" must go: off-CI the assertion can
      do neither. Cite #2148 and the CI figure. Do not touch section 6's other claims.
      Depends on T001. Disjoint from T005–T006.

- [X] **T005** [P] [US-1] `specs/087-which-assertions-cannot-fail/census.md` — two edits
      in one file:
      - `:177` margin row → sampled CI observation, floor 4.22×, class **COMFORTABLE**;
        keep the off-CI figure in the row labelled as the environment that does not
        enforce, so the row teaches the distinction rather than just correcting the
        number.
      - §2d item 7 — strike the p99 from the nine UNKNOWNs and say where the number now
        lives. **Do not renumber items 8 and 9**; `grep -rn '2d' specs/087-*` first to
        find any cite that would break.
      Depends on T001. Disjoint from T004, T006.

- [X] **T006** [P] [US-1] `specs/087-which-assertions-cannot-fail/spec.md` — three edits:
      the margin row; remove `NFR002_MqttConnectAuthTests` p99 from the "never
      recorded → UNKNOWN" row; mark **F14 void** with the reason.
      **Coordinates corrected 2026-09-12.** Planned as `:164`, `:165`, `:330`.
      `:165` was never the UNKNOWN row — it is `NFR001_AuditIngestLatencyTests`;
      the UNKNOWN row was `:166`. After this spec's own annotations the three rows
      are at **`:166`, `:168`, `:346`**.
      **Void, not deleted** — a removed finding leaves no trace that it was examined.
      Depends on T001. Disjoint from T004, T005.

- [X] **T007** [US-1] `specs/087-which-assertions-cannot-fail/tasks.md:134` — F14's
      restatement, corrected to match T006. Small and last so it cannot disagree with the
      row it restates.
      Depends on T006.

## Close

- [X] **T008** [US-1] Re-run T002's non-comment-diff check across the whole branch.
      **Done when:** it is empty — every changed line in every `.cs` file is inside a
      comment, `P50BudgetMilliseconds` is still `15` and `P99CeilingMilliseconds` is
      still `50`.
      Depends on T003–T007.

- [X] **T009** [US-1] `specs/136-the-figure-is-from-ci/verification.md` (phase 5): the
      forty verbatim trx lines, the two margin floors, the ≈ 14× / ≈ 15× ratio, the
      invariant command with its hash, the corrected records, and — honestly — what is
      **not** verified, including this branch's own CI integration run as the pending
      "after" characterisation.
      Depends on T008.

- [X] **T010** [US-1] Phase 3 gate — add **#2148** to Project #13.
      ```sh
      gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2148
      ```
      Needs the `project` scope. `item-add` prints nothing on success; verify with
      `item-list --limit 2000`, never the 30-item default.
      **Done 2026-09-12.** Verified by `item-list --limit 2000 --format json` filtered
      on `content.url` ending `/2148` → exactly 1 item. (Filtering on the item *number*
      returns zero — it is the board item's number, not the issue's.)
      Depends on nothing. **[P]** with everything.

## Parallelism summary

T001 → T002 are strictly serial and block the rest. Then **T004, T005, T006 run in
parallel** — three files, no overlap, no shared section. T003 is independent of those
three and can run alongside them; it is listed separately only because it is the one
`.cs` file and carries the ADR-0084 check. T007 waits on T006, T008 on all of them.
T010 is free at any time.

## Not done, deliberately

- **No `ci.yml` change.** Comment 1's proposed `--logger "trx"` is already at `:180`
  (`:179` is the category filter) and has been since `f085cd3e`, where it sat at `:142`.
  Nothing to add.
- **No console-verbosity change.** Spec 123 declared it out of scope for the same
  reason: it would change what every integration run prints. A separate issue if wanted.
- **No threshold moves.** Forbidden by the issue body and #2141; unmotivated at a
  sampled margin floor of 4.22× (p50) and 3.85× (p99).
- **No ADR-0100 edit.** `docs/adr/0100-…:237-239` wrongly says this test asserts
  p99 ≤ 5 ms against Testcontainers. Amending an ADR is one of the autonomous lane's
  three blocked outcomes (ADR-0144), so the discrepancy is recorded in `spec.md`'s
  *Out of scope, argued* and needs its own issue.
- **No guard test over the markdown.** Asserting a file contains a string proves the
  string was typed and breaks on rewording. T008's non-comment-diff check is the real
  mechanical assertion available here; the prose is verified by reading, at phase 6.
- **No ADR.** Nothing here is an architectural decision, which is what keeps the issue
  inside the autonomous lane's three prohibitions (ADR-0144).
