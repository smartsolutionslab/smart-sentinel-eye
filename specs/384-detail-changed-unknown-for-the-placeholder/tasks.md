# Tasks 384: `DetailChanged` is unknown when either camera address redacts to the placeholder

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2998 (feature-level; no per-task
issues).

**Phase 4a colour: RED** (behaviour-changing: the stored `DetailChanged` value for a placeholder side changes
from `true`/`false` to `null`), **plus an adversarial pass**. D1-D4 and the three re-pins compile on unchanged
code and must be seen **failing on an assertion**. C1 and C2 must arrive green and stay green unmodified.

**Engineer:** backend-engineer (T010). **Test-writer:** T001-T002. **Test-adversary:** T020.
**Security-sensitive: yes** — phase 6 runs `security-reviewer` alongside `/code-review`.
**New ADR:** no (owner decision on #2998).

**Authorised test edits (only these):** the `DetailChanged` assertion (and name) of
`AuditPayloadHostCharsetRedactionTests.Changing_between_two_different_non_ascii_addresses_still_reports_a_detail_change`,
`AuditPayloadUserInfoRedactionTests.Changing_between_two_cut_short_addresses_stores_the_placeholder_and_reports_a_detail_change`,
and the `("garbage SECRET1", "garbage SECRET2", true)` row of
`AuditPayloadRedactionPostFixAdversarialTests.DetailChanged_edges` (moved to its own fact), per plan §4.2.

**Forbidden in every task:** any other edit to an existing test file (incl. `AuditPayloadRedactionTests.cs`
and `Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.cs`); any production file other than
`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`; `UrlRedaction.cs`,
`RtspUrl.cs`, `StreamSourceUrl.cs`, `Shared.Contracts`; migrations; the UI; ADRs, the constitution,
CLAUDE.md; suppressions, thresholds, `.editorconfig`, analyzer configs.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** none — no Shared.Contracts, AppHost or Aspire change. T001 and T002 own disjoint files in
one test project and may run in parallel; T010 is the only production task.

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] New
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedUnknownTests.cs` per
  plan §4.1: D1, D2 (4 rows), D3, D4 red; C1 (2 rows), C2 (2 facts) green.
- [x] **T002** [P] [US1] The three re-pins per plan §4.2 in `AuditPayloadHostCharsetRedactionTests.cs`,
  `AuditPayloadUserInfoRedactionTests.cs`, `AuditPayloadRedactionPostFixAdversarialTests.cs`.

Run:

```
dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~AuditPayload"
```

Expected: **10 failed** (D1, D2 x4, D3, D4, the two renamed re-pins, the new
`Two_unparseable_spellings_report_the_detail_change_as_unknown`), each on an assertion such as
`should be Null but was True` / `... but was False`; every other `AuditPayload*` test passes (C1, C2, the five
remaining `DetailChanged_edges` rows and §1.2 rows 6-13 unmodified).

Commit T001-T002 together: `test(2998): pin DetailChanged as unknown when an address redacts to the placeholder (red)`.
Return the **verbatim** red output.

Gate: the orchestrator holds the verbatim red output, every red failing on an assertion and every green
passing, before dispatching T010.

## Phase 2: Green (backend-engineer)

- [x] **T010** [US1] `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` per plan §2:
  `DetailChanged` returns `bool?` (null-raw -> `false` first, then either origin `UrlRedaction.Unparseable`
  -> `null`, else the unchanged rule); `DetailChangedMember(bool?)` gains the `"DetailChanged":null` arm;
  one *why* comment. Run the three counterfactuals in plan §4.4 (each must leave its named test red), then
  plan §4.4 in full. T001-T002 green **unmodified**. Do not stop or boot a shared Aspire stack.
  Commit: `fix(2998): report DetailChanged as unknown when an address redacts to the placeholder`.

## Phase 3: Adversarial (test-adversary)

- [ ] **T020** [US1] New `AuditPayloadDetailChangedUnknownAdversarialTests.cs` per plan §4.3. Every fact
  passes against T010; a failing one is a finding returned verbatim (fix red-first as T011), not an edited
  test. Commit: `test(2998): adversarial placeholder pairs for DetailChanged`.

## Phase 4: Verify, review, PR (orchestrator)

- [ ] **T030** Phase 5 per spec §5: step 1 always; step 3 only if no other Aspire stack runs; otherwise state
  that steps 1-2 stand. Quote the T001-T002 red output and the post-fix green run in the PR body.
- [ ] **T031** Phase 6: `/code-review` and `security-reviewer` (ask it to confirm no other audit or log path
  derives a "same origin" claim from two `ToOrigin` results). Raise in the PR body: the §6 assumptions
  (null-raw precedence, the two renames, identical-placeholder = null), the third re-pin and its authorising
  clause, and that existing rows keep their stored `true` (#2990).
- [ ] **T032** Re-check the spec number (384) against `origin/develop` and every branch head (385 #2991 and
  386 #3001 are siblings) before `gh pr create --base develop`; PR body uses `Closes #2998`.

## Dependencies

T001 ‖ T002 -> T010 -> T020 -> T030 -> T031 -> T032.

## Implementation record (T010)

Status: implemented, CI pending. Counterfactuals (uncommitted scratch edits, restored and rebuilt): placeholder
arm returning `false` left 10 tests red (D1, D2 x4, D3, D4, the two re-pins, `Two_unparseable_spellings_...`);
placeholder checked only when both sides are placeholders left the 4 D2 rows red; null-raw rule after the
placeholder check left the 2 C1 rows red. After the fix: AuditObservability.Application.Tests 224 passed,
Architecture.Tests 1197 passed, Shared.Kernel.Tests 14895 passed; Release build 0 errors with S107 108,
S138 21, S104 0. Rows already written keep their stored `true` (#2990).
