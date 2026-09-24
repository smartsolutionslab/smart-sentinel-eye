# Tasks 241 — The field another branch filled

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2526 (feature-level issue; no per-task issues)
**Engineer**: `frontend-engineer` · **Phase 4a colour**: **RED** (behaviour-changing: plan §4 a–e, plus the `:244` guard once its workaround is removed).

Format: `[ID] [P?] [Story] description — file(s)`.
No foundational task: nothing in Shared.Kernel/Contracts, AppHost or Aspire resources changes. Every task touches the same two files, so nothing here is `[P]` — the tasks are sequential.

## Phase 4a — tests first (test-writer; return verbatim output)

- [ ] **T001** [US1] Add plan §4 cases a–e (spec §3 scenarios 1–5) to `apps/management-web/src/features/rules/RuleDialog.test.tsx`, using the file's existing `fill` / `fillValidRule` helpers.
- [ ] **T002** [US1] In the same file: remove the `user.clear(...)` calls and workaround comment from *"Submits a HighlightOverlay rule without the variable fields it no longer uses"* (`:244`); in *"Still asks a multi-fab operator to choose a fab after an action toggle"* (`:284`) re-fill Variable name and Value expression after the toggle back. **No `expect` line in either test changes.** Depends on T001 (same file).
- [ ] **T003** [US1] Run `pnpm --filter @smart-sentinel-eye/management-web test -- RuleDialog` on unchanged production code. Expected verbatim: **a–e red on their assertions** (DOM value or request payload — not a lookup/compile error); **`:244` red** (invalid-UUID refusal, no request); **`:284` green** (carryover still supplies its values today); every other existing case green. Depends on T002.

## Phase 4b — production (engineer; T001–T002 may not be edited)

- [ ] **T004** [US1] Add `key="set-variable"` / `key="highlight-overlay"` to the two branch wrappers and a one-line *why* comment on the ternary (plan §2). Leave the `unregister()` effect, `renderedFields`, `onSubmit` and `DEFAULT_INPUT` byte-for-byte — `apps/management-web/src/features/rules/RuleDialog.tsx`. Depends on T003.
- [ ] **T005** [US1] Counterfactual: temporarily remove one of the two `key`s, run T003's command, quote the reds, restore. Depends on T004.
- [ ] **T006** [US1] `pnpm --filter @smart-sentinel-eye/management-web test` (full), `pnpm lint`, `pnpm typecheck`, `pnpm format:check` green; T001–T002 green **unmodified**. Depends on T005.

## Phase 5

- [ ] **T007** Verification note on the PR: component-test-verified against spec §3; optionally the manual step in spec §5. Record the accepted round-trip change (spec §2, A1) in the PR body. No latency figure — not on the §IV path. Depends on T006.

## Dependencies

```
T001 → T002 → T003 → T004 → T005 → T006 → T007
```
