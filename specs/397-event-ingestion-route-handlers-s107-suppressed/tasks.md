# Tasks 397: the EventIngestion route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2971 (feature-level;
no per-task issues; already on Project #13).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving; no executable line
changes). No new tests (spec D-4): T001 runs the existing covering suites on unchanged code and its
output is quoted verbatim in the PR; the same commands pass after T010-T014 with
`git diff origin/develop -- tests/` empty. An assertion that would have to change is a **block**.

**Engineer:** backend-engineer (T010-T014). **Test-writer:** T001 only (runs, writes nothing).
**Review:** `/code-review` (backend-reviewer); not security-sensitive (spec, Lane).

**Forbidden in every task:** any file under `tests/`; any handler signature, body, or `Map*` chain;
`UnconditionalSuppressMessage`; a `SuppressMessage` on anything but the ten handlers of spec §1
(not `GetEvent`, any `List`, `WebhookIntegrationsEndpoints.Register`, `RegisterAsync`, `DeclareAsync`,
`AuthenticateWebhookAsync`, any helper); `#pragma warning disable`; `.editorconfig`, `SonarLint.xml`,
`Directory.Build.props`, `Directory.Packages.props`, csproj files, `GlobalSuppressions.cs`; `Log.cs`;
ADRs, the constitution, CLAUDE.md; any Domain/Application/ServiceDefaults S107 hit.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks (ADR-0109). No foundational task:
nothing touches Shared.Kernel, Shared.Contracts or AppHost. T011-T014 are file-disjoint; commits
still land sequentially (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** [US1] Re-run plan §4.1 on the branch tip (unchanged code). Expect spec §1's 23
  lines (10 Api S107 + 13 others). If the list differs, stop and re-plan — the premise moved.

## Phase 2: Characterisation, observed green (test-writer)

- [ ] **T001** [US1] Run plan §4.3's filtered `Architecture.Tests` command on unchanged code.
  Return the summary line verbatim (passed count must be non-zero, failed zero). Writes no file.
  Depends on T000.

## Phase 3: The change (backend-engineer; one commit per file, each builds and stays green)

- [ ] **T010** [US1] `src/EventIngestion/Api/EventsEndpoints.Reads.cs`: add the `using` and plan
  §2.1's attribute to `ListEvents` only; build; confirm H3 gone. Then plan §4.2's counterfactual
  (remove, rebuild, H3 returns, restore) — quote both build lines. Add the attribute to
  `ListDeadLetters`. Commit. Depends on T001. **Gate for T011-T014**: if H3 does not disappear,
  stop (spec A-2 false) and report.
- [ ] **T011** [P] [US1] `src/EventIngestion/Api/EventsEndpoints.Writes.cs`: `IngestManual`,
  `IngestWebhook`. Commit. Depends on T010.
- [ ] **T012** [P] [US1] `src/EventIngestion/Api/EventSourcesEndpoints.cs`: `Declare`, `Change`,
  `Undeclare` (attribute after `Undeclare`'s `/// </summary>`). Commit. Depends on T010.
- [ ] **T013** [P] [US1] `src/EventIngestion/Api/EventTypesEndpoints.cs`: `Register`, `Retire`.
  Commit. Depends on T010.
- [ ] **T014** [P] [US1] `src/EventIngestion/Api/WebhookIntegrationsEndpoints.cs`: `Revoke`.
  Commit. Depends on T010.

## Phase 4: Verify (orchestrator)

- [ ] **T020** [US1] Re-run plan §4.1: sorted list equals spec §1's 13 lines exactly (SC-1, SC-2).
  Re-run T001's command unmodified; quote it (SC-3). `git diff --stat origin/develop -- tests/`
  empty; `git diff --stat origin/develop -- src/` names only the five files (SC-5);
  `grep -rn '"S107:' src/EventIngestion` returns exactly 10 lines. Depends on T011-T014.
- [ ] **T021** Confirm plan §6 (form identical to spec 398's) before opening the PR. PR body:
  `Closes #2971`, the T001/T020 test summaries, the before/after warning lists, the counterfactual
  lines. After merge, check #2971 is closed. Depends on T020.

## Dependencies

T000 → T001 → T010 → {T011, T012, T013, T014} → T020 → T021.
