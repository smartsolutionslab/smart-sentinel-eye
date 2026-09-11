# Tasks 136 — The figure is from CI

Declared at phase 3: **behaviour-preserving** → phase 4a is **characterisation, observed
green**. Engineer: **none required for judgement** — the work is documentation plus a
comment block. Assign **infra** if one is needed, because the evidence lives in `ci.yml`
artifacts and the reader must be fluent with `gh run download`; no backend or frontend
skill is exercised.

All tasks are US-1. Grouping is by file ownership, so the `[P]` markers are real
disjoint-file parallelism (ADR-0109).

## Foundational — blocks everything

- [X] **T001** [US-1] Re-run the measurement on **four** current green `develop` runs
      and record the figures. Do not reuse this spec's table if more than a few days
      have passed — artifact retention is 14 days, and the first run after machine churn
      reads like a regression, which is why the count is four and not one.
      ```sh
      gh run list --workflow ci.yml --branch develop --status success --limit 4 \
        --json databaseId -q '.[].databaseId' | while read -r R; do
        gh run download "$R" -n integration-test-results -D "/tmp/ci-$R"
        grep -rohE 'CONNECT.CONNACK over [^<]{0,160}' "/tmp/ci-$R"
      done
      ```
      **Done when:** four lines, each ending `(budgets enforced)`, with p50 and p99
      values and their margins against 15 ms and 50 ms written down.
      Blocks T002–T007.

- [X] **T002** [US-1] Phase 4a characterisation, **green**. Establish the two facts the
      later tasks must preserve:
      (a) the four T001 lines are the "before" reading of the **unmodified** file;
      (b) `git diff` of `NFR002_MqttConnectAuthTests.cs` restricted to non-comment lines
      is empty at every later point.
      **Done when:** the verbatim four lines are captured, and the non-comment-diff check
      is written down as the command it is, ready to re-run at T008.
      Depends on T001.

## The point of enforcement

- [X] **T003** [US-1] Add a **CI paragraph** to the `<remarks>` of
      `tests/Integration.Tests/Identity/NFR002_MqttConnectAuthTests.cs`, beside spec
      123's off-CI paragraph at `:79-90` — not replacing it. It carries: the four run
      figures and ids, p50 margin 6.6×–7.6×, p99 margin 5.6×–11.0×, the ≈ 14×
      environment ratio, and one sentence separating the 50 ms wall-clock ceiling from
      NFR-002's 5 ms auth-overhead SLO (ADR-0100, `specs/008-*/spec.md:425`).
      **Must not touch:** `:51`, `:55`, `:92-93`, `:130-132`, `:138`, `:141`.
      **Check ADR-0084's 300 LOC/file limit before and after**; if it would breach, the
      figures go to `verification.md` and the remarks keep a two-line summary and a cite.
      Depends on T002. Owns this file exclusively.

## The four records — three parallel lanes, disjoint files

- [X] **T004** [P] [US-1] `specs/021-transactional-outbox/verification.md:212-215`.
      Rewrite the CONNECT→CONNACK sentence so it reads as an off-CI, budgets-not-enforced
      observation. Both "breached" and "passed after" must go: off-CI the assertion can
      do neither. Cite #2148 and the CI figure. Do not touch section 6's other claims.
      Depends on T001. Disjoint from T005–T006.

- [X] **T005** [P] [US-1] `specs/087-which-assertions-cannot-fail/census.md` — two edits
      in one file:
      - `:177` margin row → CI observation, 6.6×–7.6×, class **COMFORTABLE**; keep the
        off-CI figure in the row labelled as the environment that does not enforce, so
        the row teaches the distinction rather than just correcting the number.
      - §2d item 7 — strike the p99 from the nine UNKNOWNs and say where the number now
        lives. **Do not renumber items 8 and 9**; `grep -rn '2d' specs/087-*` first to
        find any cite that would break.
      Depends on T001. Disjoint from T004, T006.

- [X] **T006** [P] [US-1] `specs/087-which-assertions-cannot-fail/spec.md` — three edits:
      `:164` margin row (as T005); `:165` remove `NFR002_MqttConnectAuthTests` p99 from
      the "never recorded → UNKNOWN" row; `:330` mark **F14 void** with the reason.
      **Void, not deleted** — a removed finding leaves no trace that it was examined.
      Depends on T001. Disjoint from T004, T005.

- [X] **T007** [US-1] `specs/087-which-assertions-cannot-fail/tasks.md:132` — F14's
      restatement, corrected to match T006. Small and last so it cannot disagree with the
      row it restates.
      Depends on T006.

## Close

- [X] **T008** [US-1] Re-run T002's non-comment-diff check across the whole branch.
      **Done when:** it is empty — every changed line in every `.cs` file is inside a
      comment, `P50BudgetMilliseconds` is still `15` and `P99CeilingMilliseconds` is
      still `50`.
      Depends on T003–T007.

- [ ] **T009** [US-1] `specs/136-the-figure-is-from-ci/verification.md` (phase 5): the
      four verbatim trx lines, the two margins, the ≈ 14× ratio, the five corrected
      records with their before/after text, and this branch's own green integration run
      as the "after" characterisation.
      Depends on T008.

- [ ] **T010** [US-1] Phase 3 gate — add **#2148** to Project #13.
      ```sh
      gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2148
      ```
      Needs the `project` scope. `item-add` prints nothing on success; verify with
      `item-list --limit 2000`, never the 30-item default.
      Depends on nothing. **[P]** with everything.

## Parallelism summary

T001 → T002 are strictly serial and block the rest. Then **T004, T005, T006 run in
parallel** — three files, no overlap, no shared section. T003 is independent of those
three and can run alongside them; it is listed separately only because it is the one
`.cs` file and carries the ADR-0084 check. T007 waits on T006, T008 on all of them.
T010 is free at any time.

## Not done, deliberately

- **No `ci.yml` change.** Comment 1's proposed `--logger "trx"` is already at `:179` and
  has been since `f085cd3e`. Nothing to add.
- **No console-verbosity change.** Spec 123 declared it out of scope for the same
  reason: it would change what every integration run prints. A separate issue if wanted.
- **No threshold moves.** Forbidden by the issue body and #2141; unmotivated at 6.6×.
- **No guard test over the markdown.** Asserting a file contains a string proves the
  string was typed and breaks on rewording. T008's non-comment-diff check is the real
  mechanical assertion available here; the prose is verified by reading, at phase 6.
- **No ADR.** Nothing here is an architectural decision, which is what keeps the issue
  inside the autonomous lane's three prohibitions (ADR-0144).
