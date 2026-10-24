# Tasks 450: A stored address holding a credential is masked on read, not thrown

[spec](spec.md) · [plan](plan.md) · Issue #3118 (feature-level; on Project #13 — verify by `content.url`)

**Phase 4a colour: RED** (new behaviour). **Security-sensitive: yes** — phase 6 runs `security-reviewer` and
`/code-review`. **Engineer: backend-engineer only.** **ADR: none needed** (plan §ADR check).
No foundational task: no Kernel, Contracts, AppHost or migration change. T001-T003 own disjoint files and run `[P]`.

**Forbidden:** `StreamSourceUrl` (follow-up, spec §4); `UrlRedaction`; both read handlers; the address route; any
migration or stored-row rewrite (#2990); ADRs, the constitution, CLAUDE.md; suppressions, thresholds, analyzer
configs; any existing assertion other than `RtspUrlFromStoredTests.cs:32-33` (and its class summary sentence) and the 39
retargeted cases listed in plan §Owner approval and retargeted cases (owner approval: comment on issue #3118,
2026-10-24).

## Phase 4a — red (test-writer)

- [x] T001 [P] [US1] R1: move the two userinfo rows of `RtspUrlFromStoredTests` into a new accept theory with the four
      plan rows; amend the summary. Run `CameraCatalog.Domain.Tests`; **observe red, quote** the
      `ArgumentException` lines.
- [x] T002 [P] [US1] R2: new `tests/Architecture.Tests/Persistence/CameraUrlUserInfoHydrationTests.cs` (converter
      reads the rows back). Run it; **observe red, quote**.
- [x] T003 [P] [US1] R3: new `tests/Integration.Tests/CameraCatalog/StoredCredentialCameraAddressIntegrationTests.cs`
      **plus its `ci-shards/shard-1.filter` entry**. Compiles locally; red (500 on both reads) observed locally if a
      stack is free, otherwise argued by T002 and observed green in CI — say which in the PR.
- [x] T004 Commit the red set (`test(3118): ...`), each commit building on its own.

## Phase 4b — implementation (backend-engineer; may not edit any test) — depends on T004

- [x] T010 [US1] `RtspUrl.cs`: move the userinfo clause from `EnsureStoredInvariants` to the first `.Satisfies` of
      `From`; update the `FromStored` comment and the class summary. R1, R2 green; every `From` test unmodified green.
- [ ] T011 Counterfactuals 1-2 (plan), each failure quoted, each reverted.
- [x] T012 (build and format verified; coverage not measured locally) `dotnet build -c Release` clean; `dotnet format --verify-no-changes` on touched files; CameraCatalog
      Domain coverage ≥ 90 %. Commit `fix(3118): ...`.

## Phase 5 — verify

- [ ] T020 R3 green (CI job log of the PR's tip, or local run); counterfactual 3 if a stack is free. Latency: N/A.

## Phase 6 — review

- [x] T030 `security-reviewer` (plan §Security review focus) and `/code-review`; findings resolved or accepted in writing.
- [ ] T031 Recommend (do not file without checking the board) the `StreamSourceUrl.FromStored` sibling as a follow-up.
