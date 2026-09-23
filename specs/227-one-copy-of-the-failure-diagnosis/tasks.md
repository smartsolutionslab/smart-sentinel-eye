# Tasks 227 — One copy of the failure diagnosis

**Spec:** [`spec.md`](spec.md) · **Plan:** [`plan.md`](plan.md) · **Issue:** #2294

Declared at phase 3: **behaviour-preserving** → phase 4a is **characterisation, observed
green**. Engineer: **backend**. No ADR — spec §Locked tech choices.

Everything below is **US-1**. US-2 (13 other-named copies) and US-3 (3 body-only
`BodyAsync`) are **not delivered**; T011 files them.

**Parallelism (ADR-0109).** T003 (shared method) is foundational: the nine forwarders do
not compile without it. After it, T004–T005 own disjoint files and may run `[P]`, but they
are ~2 lines each; one engineer in one pass is cheaper than a fan-out. The guard edit
(T006) is independent of the forwarders in *files*, but its counterfactual (T008) needs
both.

---

## Foundational

- [ ] **T000** [US-1] **Do not boot the Aspire fixture on this machine.** C: at 95%
      (13 GB free, 2026-09-23). No `dotnet test` of `tests/Integration.Tests` locally.
      **Done when:** acknowledged; the only local test run is `Architecture.Tests`.

- [ ] **T001** [P] [US-1] **Baseline trx.** In hand: run `35894668870`, SHA `b8c14eb9…`,
      49/49 `Passed`, shards 156/157/158/169 all `failed="0"`. Re-capture only if picked up
      after **2026-10-07** (artifact expiry), with spec §procedure's commands.
      **Done when:** the 49 lines and four `<Counters>` lines are in the PR-body draft with
      run id and SHA.

- [ ] **T002** [P] [US-1] **Reproduce all five phase-3 hashes on the unmodified tip**
      (plan §Invariance checks, (1)–(5)). A mismatch means the baseline was taken on a
      different tree — stop and report.
      **Done when:** five hashes printed and matched.

## The change

- [ ] **T003** [US-1] **Add `tests/Integration.Tests/Fixtures/AspireFixture.Diagnostics.cs`**
      with `public async Task<string> DiagnoseAsync(string resourceName,
      HttpResponseMessage response)`, body **verbatim** from plan §1 (body read before
      `RecentLogs`; no `ConfigureAwait`; no guard). One `<summary>` explaining why.
      **Done when:** it compiles, and its format string is character-for-character the one
      in spec finding A.

- [ ] **T004** [P] [US-1] **Forwarders, Automation + Identity** (5 files):
      `Automation/RuleLifecycleIntegrationTests.cs`,
      `Automation/CrossFabEvaluationIntegrationTests.cs`,
      `Identity/FabGroupClaimIntegrationTests.cs`,
      `Identity/RegisteredClientConcurrencyIntegrationTests.cs`,
      `Identity/TokenAttributionIntegrationTests.cs`. Replace each private
      `DiagnoseAsync` body with
      `private Task<string> DiagnoseAsync(HttpResponseMessage response) => aspire.DiagnoseAsync("<literal>", response);`
      using plan §2's table; delete the per-copy doc comment. **No call site is edited.**
      Depends on T003.

- [ ] **T005** [P] [US-1] **Forwarders, EventIngestion + LayoutComposition +
      OverlayDesigner** (4 files):
      `EventIngestion/WebhookIntegrationConcurrencyIntegrationTests.cs`,
      `EventIngestion/WebhookRevocationRefusesDeliveryIntegrationTests.cs`,
      `LayoutComposition/LayoutNameUniquenessIntegrationTests.cs`,
      `OverlayDesigner/OverlayNameUniquenessIntegrationTests.cs`. Same as T004.
      Depends on T003.

- [ ] **T006** [P] [US-1] **Widen the guard's pattern** in
      `tests/Architecture.Tests/LogTailCoverageTests.cs:47–49` to
      `@"\b(?:RecentLogs|DiagnoseAsync)\(\s*""([^""]+)"""` and add the one sentence to its
      `<summary>` (plan §3). Do **not** touch `Explain` or any `.Should` statement.
      Independent of T003–T005 by file.

- [ ] **T007** [US-1] **Build and fast tests.** Release build of the solution clean
      (analyzers, `TreatWarningsAsErrors`); `dotnet test tests/Architecture.Tests` green —
      including `LogTailCoverageTests`, `PrimitiveBoundaryTests`,
      `HandlerDeconstructionTests`. `git diff --name-only` lists exactly **11** files: the
      nine, the new partial, the guard (plus this spec directory).
      Depends on T004, T005, T006.

## Gate — mechanical checks

- [ ] **T008** [US-1] **Guard counterfactual.** Set one forwarder's literal to
      `"no-such-resource"`; run
      `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~LogTailCoverageTests`
      → must be **red**, naming that file and `'no-such-resource'`. Revert → green. Quote
      the red output verbatim in the PR.
      **Done when:** red observed and quoted, then green restored, `git diff` unchanged
      from T007.

- [ ] **T009** [US-1] **Re-run the five invariance checks** (plan (1)–(5)). All five must
      print the phase-3 hashes. **If any differs, stop and report** — adjusting an
      assertion, `assertions.sh` or the guard to restore it is a blocked outcome
      (ADR-0144). Also: `grep -n 'RecentLogs(' ` over the nine files prints nothing
      (each file's only log request was inside `DiagnoseAsync`, verified at phase 3).
      **Done when:** five hashes quoted in the PR body.

## Phase 5 — behavioural evidence

- [ ] **T010** [US-1] **This PR's trx.** When the four `integration tests (Docker)` shards
      conclude, run spec §procedure against this PR's run id: the same 49 names
      `Passed`, four `failed="0"`, quoted beside the before-figures. A red covering test
      is a **stop** → `agent:blocked`, not an adjustment. Write down what the trx does
      not prove (spec §procedure, last paragraph).

## Closing out

- [ ] **T011** [US-1] **PR + follow-ups + board.**
      - PR body: before/after trx (T001/T010), five hashes (T009), the counterfactual
        (T008), `Phase 4a: characterisation, observed green`, and a statement that
        **#2294 is not closed by this PR** — 13 other-named copies (US-2), the body-only
        divergence (US-3), and the `UniqueName`/`ClientFor` re-scope remain. Use
        `Refs #2294`, not a closing keyword. Base `develop`; Conventional Commits
        (`refactor(tests): …`); no `Co-Authored-By` (ADR-0086).
      - File **US-2**: *"Ten `BodyAsync` and three `Diagnose` helpers are the same failure
        diagnosis as `AspireFixture.DiagnoseAsync`"* — list the 13 files (spec finding B).
      - File **US-3**: *"Three `BodyAsync` helpers report the body without the service
        log"* — `VariableReadScope`, `ReverseIndexSeedCredential`, `RuleRead`;
        behaviour-changing.
      - Comment on #2294: the re-measured counts (22/20/13/9) and that `UniqueName` /
        `ClientFor` differ per file by design, so "one shared home" does not apply as
        written.
      - Board: #2294 is **already** on the project (phase 3 checked
        `gh issue view 2294 --json projectItems` → "Smart Sentinel Eye"). Add the two
        follow-ups with `gh project item-add 13 --owner smartsolutionslab --url <url>`.
      **Done when:** follow-ups exist with numbers and are linked from the PR.

---

## Dependencies

```
T000 ─┬─> T001 [P] ───────────────────────────────────────────> T010 ─> T011
      └─> T002 [P] ─> T003 ─┬─> T004 [P] ─┐
                  └─> T006 [P] ───────────┼─> T007 ─> T008 ─> T009 ─┘
                            └─> T005 [P] ─┘
```

T002 strictly before any edit. T003 blocks T004/T005 (compile). T008 needs the forwarders
and the widened guard both in place.
