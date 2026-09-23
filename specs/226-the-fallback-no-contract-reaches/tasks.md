# Tasks — Spec 226, the fallback no contract reaches

**Spec:** `specs/226-the-fallback-no-contract-reaches/spec.md`
**Plan:** `specs/226-the-fallback-no-contract-reaches/plan.md`
**Issue:** #2504 (on Project #13, status Todo — verified with `--limit 2000`)
**Branch:** `2504-dead-entries-no-contract-needs`

---

## The colour, declared once (constitution §Testing, ADR-0144 phase 4a)

**Characterisation, observed GREEN.** This is behaviour-preserving: it deletes
source that cannot execute. The 20-row table in
`tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`
is the covering test; it is captured **passing before** any source edit and must
pass **unmodified after**. An assertion that has to be edited is evidence the
behaviour moved — **block, do not adjust** (ADR-0144).

**And green alone does not discharge the gate here.** Spec §0.4 establishes that
this table is green under the prune, green under pruning all twelve entries, and
green under deleting the fallback entirely. T006–T008 supply the sensitivity the
table lacks. A PR quoting only T001's green output has not proven the change is
safe; it has proven the alarm is switched off.

---

## US1 — The dead entries go, and what makes them dead is written down (P1)

### Baseline — before any source edit

- **[T001] [US1]** Capture the characterisation baseline. Run
  `dotnet test tests/AuditObservability.Application.Tests --filter FullyQualifiedName~V1ResourceMapTests`
  on the untouched tree and record the **verbatim** output, including the
  `Passed:` count. Spec §9(4) predicts 28 (20 theory rows + 8 facts); if it
  differs, the **observed** number is the baseline and the spec line is wrong.
  Also run `git diff --stat` and `git stash list` to record that nothing is
  hidden. **Blocks everything.**

- **[T002] [US1]** Decide FR-007's form (plan §5.2) and record the decision in
  one line before writing any assertion. `AuditObservability.Application` grants
  no `InternalsVisibleTo` today — re-confirm, then choose: **preferred** (add the
  grant, derive the convention-mapped set from `Conventions.HandTweaks` and
  `NamespaceToResource`) or **fallback** (no project edit, one named exception,
  `WebhookIntegrationRotatedV1`). Do **not** hard-code the seven hand-tweak
  names. Depends on: T001.

### The guard, written and observed green *before* the prune

- **[T003] [US1]** Write
  `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapFallbackReachabilityTests.cs`
  in the form T002 chose. Sentence-style name (ADR-0053), Shouldly (ADR-0052),
  no leading-underscore field, explicit-type collection expressions. The doc
  comment states which form was used and why, and cites spec §0.2. Depends on:
  T002.

- **[T004] [US1]** Run T003's guard on the **untouched** source and record it
  **green**. This is characterisation, not new behaviour: it pins a property the
  corpus already has. A guard that is red here means spec §0.2's structural
  argument is wrong — **stop and report**, do not adjust the assertion. Depends
  on: T003.

### The prune

- **[T005] [US1]** Apply FR-001 through FR-005 as **one** commit-sized change:
  - `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs` —
    delete the seven entries from `IdentifierPropertyNames` (`:33-47`), leaving
    `Name`, `OverlayIdentifier`, `RegisteredClientIdentifier`, `ChunkIdentifier`,
    `EventIdentifier` in their current relative order; replace the two
    `Shared.Contracts.Automation` illustrations (`:15-20`, `:138-139`) with a
    real namespace; add FR-004's sentence to the `BuildConventionPicker` comment.
  - `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`
    — delete `["Automation"] = DomainResourceKind.Rule` (`:34`).
  - `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs` — doc comment on
    `Rule` (`:23`) in the shape of `Webhook`'s (`:31-42`). **Do not touch
    `All`.**

  Nothing else. In particular **do not touch
  `tests/.../EventHandlers/V1ResourceMapTests.cs`** (D3). Depends on: T004.

### The counterfactual — spec §6 Part B, SC-004

This is the evidence. It is three tasks because each produces a separate quoted
output and skipping the middle one is the failure mode.

- **[T006] [US1]** On a tree with T005 **reverted** (`git stash` the prune),
  add the throwaway
  `src/Shared.Contracts/CameraCatalog/ThrowawayNoGuidV1.cs` —
  `sealed record ThrowawayNoGuidV1(string CameraIdentifier, EventMetadata Metadata) : IIntegrationEvent`,
  no `Guid` anywhere — plus a scratch test asserting
  `V1ResourceMap.Default.Lookup` returns kind `camera` and the
  `CameraIdentifier` value. Run it, record the output **verbatim**. This is the
  allow-list fallback executing — the only time in this spec's life that it
  does. Depends on: T005.

- **[T007] [US1]** Record the **expected collateral reds** from T006's
  throwaway contract, by name, and confirm each is a guard working rather than a
  break: `V1ResourceMapTests.Every_integration_event_has_a_row_in_the_mapping_table`,
  `BoundaryTests.V1ResourceMap_covers_every_IIntegrationEvent`, and the
  `IntegrationEventAuditHandler` `Handle`-entry-point boundary guard. **Do not
  fix, suppress, or adapt any of them** — adapting a guard to accommodate a
  throwaway is weakening a gate (ADR-0144). Depends on: T006.

- **[T008] [US1]** Re-apply T005's prune on top of the throwaway and re-run
  T006's scratch test. Expect kind `camera` and **no** identifier. Record the
  output verbatim. Then **revert the throwaway contract and the scratch test
  entirely** and confirm `git status` shows only the intended files. The pair
  T006/T008 is the falsifiable demonstration that the pruned entries were
  reachable in principle and that no contract in the corpus reaches them.
  Depends on: T007.

### Verification

- **[T009] [P] [US1]** Re-run T001's exact command. Expect the identical
  `Passed:` line, and `git diff -- tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`
  to print **nothing**. Quote both the before and after output in the PR
  (NFR-001, SC-003). Depends on: T008.

- **[T010] [P] [US1]** Re-run T003's guard and the rest of the context's suites:
  `dotnet test tests/AuditObservability.Application.Tests`,
  `dotnet test tests/AuditObservability.Domain.Tests`,
  `dotnet test tests/Architecture.Tests`. All green — including the three guards
  of T007, which must be green again after T008's revert. Depends on: T008.

- **[T011] [P] [US1]** `dotnet build -c Release` — analyzers clean, no new
  warning, no suppression added, no analyzer narrowed (NFR-003). Then spec §6
  Part C: `ls src/Shared.Contracts/` shows eight folders and no `Automation`;
  `grep -rn "Shared.Contracts.Automation" --include=*.cs src tests | grep -v obj/`
  returns nothing. Depends on: T008.

---

## Wrap-up

- **[T012]** Re-check the spec number before opening the PR. 226 was free
  against `origin/develop` and all 12 remote branches at dispatch (highest:
  `225-the-render-leg-ci-never-reads`), but a concurrent architect dispatch can
  claim it (`spec-number-check-origin-develop-isnt-enough`). Re-run
  `git fetch origin` and the all-branches `specs/` scan; rename the directory if
  it collided. Depends on: T011.

- **[T013]** PR to **`develop`** (`gh pr create --base develop`, ADR-0028).
  Body must carry, as quoted verbatim blocks:
  1. T001's before-output and T009's after-output, with the empty `git diff` on
     `V1ResourceMapTests.cs`;
  2. T004's green guard run;
  3. **T006's and T008's counterfactual outputs** — without these the PR does
     not satisfy SC-004 and phase 6 should reject it;
  4. T007's three collateral-red names and their post-revert green.
  Commit messages: Conventional Commits, **no `Co-Authored-By` footer**
  (ADR-0030, ADR-0086). Reference #2504 with a closing keyword and check the
  issue state after the merge (`pr-mention-auto-closes-the-issue`). Depends on:
  T012.

---

## Parallelism, stated once

**This slice is almost entirely serial, and pretending otherwise would be
wrong.** T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 is a strict
chain: each step's input is the previous step's tree state, and the
counterfactual in particular depends on stashing and re-applying the very change
under test. Marking any of them `[P]` would invite two agents into one index
(`one-branch-one-index`).

Only the three verification tasks are genuinely `[P]` — **T009, T010, T011** own
disjoint commands over the same finished tree and write no files (ADR-0109).
They can run concurrently.

**No foundational blocker for the orchestrator to fan out behind.** Nothing in
`Shared.Kernel`, `Shared.Contracts`, `AppHost` or Aspire resources changes; no
other issue is unblocked by this one landing.

---

## Dependency graph

```
T001 (baseline, green)
  └─ T002 (choose FR-007 form)
       └─ T003 (write the guard)
            └─ T004 (guard green on untouched source)
                 └─ T005 (the prune)
                      └─ T006 (counterfactual: fallback fires, pre-prune)
                           └─ T007 (collateral reds, named not fixed)
                                └─ T008 (counterfactual: no identifier, post-prune; revert)
                                     ├─ T009 [P] characterisation re-run + empty diff
                                     ├─ T010 [P] full suites
                                     └─ T011 [P] Release build + §6 Part C
                                          └─ T012 (spec-number re-check)
                                               └─ T013 (PR)
```

---

## Phase-3 gate checklist

- [x] Tasks are atomic and each names its file(s) and its expected output.
- [x] Dependencies explicit; `[P]` only on disjoint, file-writing-free tasks
      (ADR-0109).
- [x] Phase-4a colour declared: **characterisation, GREEN**, with the
      counterfactual that makes green mean something.
- [x] Issue #2504 is on Project #13 (status Todo) — verified with
      `gh project item-list 13 --owner smartsolutionslab --limit 2000`, matched
      on `content.url`, not on a number filter
      (`project-13-task-issues`).
- [x] Spec references ≥ 1 ADR (spec §Preamble names eleven).
- [x] No `[NEEDS CLARIFICATION]` remains.
- [ ] **Gate:** hand back for review. Do not advance to phase 4 without it.
