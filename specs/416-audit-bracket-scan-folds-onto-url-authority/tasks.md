# Tasks 416: the audit's bracket scan folds onto `UrlAuthority`

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3023 item 6 (feature-level; no per-task
issues; already on Project #13).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). C-F0 and C-F1 are captured green on
`d24ee84e` and must pass **unmodified** after the fold. A C-F1 or C-F0 assertion that would have to change is a
**block**, never an edit (ADR-0144). The single pre-declared exception is the G2 structure retarget (spec §1.4,
FR-004); no other existing test line may change except `Generate`'s `private` → `internal`.

**Roles.** test-writer: T110, T111, T150, T151. backend-engineer: T140 (tests read-only). backend-reviewer: T180.
Phase 6: `/code-review` (no `/security-review`: the stored origin is untouched).

**Parallelism.** None. Two production files and one oracle, strictly sequential (ADR-0109 `[P]` does not apply).
No foundational task.

## Phase 0: Baseline (architect; done)

- [x] **T100** On `d24ee84e`: `AuditPayloadRedaction.cs` byte-identical to `00a66722`; `BracketedHostReadings`
  unchanged since `00a66722`; corpus is six seeds (spec §1.3); `The_audit_redaction_asks_the_shared_locator`
  conflicts with the fold (spec §1.4); fold-with-arm derived equivalent on every input (spec §1.2).

## Phase 1: Characterisation, observed green before the fold (test-writer) — commit 1

- [ ] **T110 [US1]** C-F0: run plan §4.1's five commands on `d24ee84e` (stop any running AppHost first) and quote
  passed/failed/skipped verbatim. No file change.
- [ ] **T111 [US1]** Create `tests/Architecture.Tests/AuditPayloadBracketScanOracle.cs` (plan §4.2, verbatim copy,
  source commit and lines in its summary) and `tests/Architecture.Tests/AuditPayloadBracketFoldCharacterisationTests.cs`
  (plan §4.3: corpus, explicit rows, both directions, reach floors measured and written with their figures). Change
  `CameraUrlZoneDelimiterFuzzAdversarialTests.Generate` from `private` to `internal` and no other line. Run green;
  quote verbatim. Run CF-3 (plan §4.3) against today's code, quote the failure, revert. Depends on T110.
  Commit: `test(3023): characterise the audit's zone answer against an oracle copy of today's bracket scan`.

**Gate:** T110-T111 green and quoted, floors measured (spec A-1 block line respected), CF-3 red and quoted.

## Phase 2: The fold (backend-engineer; tests read-only except T141) — commit 2

- [ ] **T140 [US1]** Plan §3: add `UrlAuthority.BracketContentIsAddressOnly` in `src/Shared.Kernel/UrlAuthority.cs`;
  in `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` delete the private
  `BracketContentIsAddressOnly` and `IsAddressText`, call the new member from `CarriesZone`, update the two comments.
  Depends on T111.
- [ ] **T141 [US1]** In the same commit: retarget `The_audit_redaction_asks_the_shared_locator` in
  `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` to G2 (plan §4.4) and add
  `Only_UrlAuthority_declares_the_bracket_content_predicate`; update the class summary's spec 392 sentence. This is
  the one pre-declared edit; it is a structure guard, decided at phase 3. Depends on T140.
- [ ] **T142 [US1]** Re-run C-F0 and C-F1 **unmodified**: counts identical to T110/T111 (Architecture differs only by
  T141's added test, stated). Run CF-1 and CF-2 (plan §4.3) and G2's counterfactual, each red, quoted, reverted.
  `git diff <commit1> -- tests/Architecture.Tests/AuditPayloadBracketScanOracle.cs tests/Architecture.Tests/AuditPayloadBracketFoldCharacterisationTests.cs tests/AuditObservability.Application.Tests`
  is empty. Commit: `refactor(3023): the audit asks UrlAuthority whether a bracketed host holds only an address`.
  Depends on T141.

## Phase 3: Post-fold guards, green on arrival (test-writer) — commit 3

- [ ] **T150 [US1]** `tests/Architecture.Tests/UrlAuthorityBracketContentOracleEquivalenceTests.cs` (G3, plan §4.4),
  whole corpus plus explicit rows, oracle-true/false floors. Depends on T142.
- [ ] **T151 [US1]** `tests/Shared.Kernel.Tests/UrlAuthorityBracketContentDirectTests.cs`: direct `true`/`false` rows
  and the null-guard row. Depends on T142. Commit (with T150):
  `test(3023): the shared bracket scan answers as the oracle on every input`.

## Phase 5-7

- [ ] **T160 [US1]** Phase 5: `Phase 5: explicit run/test` (ADR-0037). Evidence: T110/T142 identical counts, C-F1
  green before and after, CF-1..CF-3 and G2 counterfactual red. No runtime behaviour to observe (no endpoint, no
  stack path changes).
- [ ] **T180 [US1]** Phase 6: backend-reviewer confirms `git diff d24ee84e -- tests` is additions only, plus the one
  `internal` and the T141 retarget; the oracle is byte-identical to `AuditPayloadRedaction.cs:121-202` at `d24ee84e`
  (modulo `private`/`internal` and class); no `Option`/guard/metric regressions. PR body says
  `Phase 4a: characterisation`, quotes T110/T111/T142, names the retarget, `Refs #3023`.

## Dependencies

```
T100 → T110 → T111 → T140 → T141 → T142 → T150, T151 → T160 → T180 → PR to develop
```

**Block conditions** (each `agent:blocked`, not an edit): any C-F0/C-F1 assertion must change; a reach floor below
spec A-1's block line; the orchestrator reads ADR-0144 §4a item 4 as covering the T141 structure retarget (then the
owner decides).
