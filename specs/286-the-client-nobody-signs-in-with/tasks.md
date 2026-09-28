# Tasks: Spec 286, the client nobody signs in with

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2488 · **Lane:** autonomous (ADR-0144)

**Phase-4a colour:** **SPLIT.** T002-T005 are **characterisation** (observed green against the
unedited realm, then must pass **unmodified**). T007-T008 are **red** (observed failing before the
realm edit; failure quoted in the PR body). Ambiguity resolves to red — there is none here: the
split follows plan §4 fact by fact.

**Engineers:** `test-writer` (4a: T001-T009), then `infra-engineer` (4b: T010-T012). No
`backend-engineer` — no production C# changes.

One story (US1), so every task is `[US1]`. `[P]` marks tasks owning disjoint files with no
ordering dependency on their neighbour (ADR-0109).

**Board.** Issue #2488 is already on Project #13 (verified 2026-09-28).

**Contention file** (ADR-0109): `src/AppHost/Realms/smart-sentinel-eye-realm.json`. Check no
parked PR edits it before opening the PR.

**Foundational:** T001 blocks T003-T004 (they call the helper). Everything in 4b is blocked by
the whole of 4a.

---

## Phase 4a: tests first (`test-writer`)

### Characterisation (behaviour-preserving)

- [ ] **T001 [US1]** Add `PlantPasswordGrantClientAsync(string clientId, IReadOnlyList<string>
  defaultClientScopes, CancellationToken)` to `tests/Integration.Tests/Identity/RealmProbe.cs`
  (plan §D2): public, standard flow off, service accounts off, password grant on, given defaults,
  no optional scopes, success asserted like `PlantAsync`. Do not touch
  `EventTypeRegistryAuthorizationIntegrationTests`.
- [ ] **T002 [P] [US1]** Update the one sentence in `tests/Architecture.Tests/LegacyBundleGrantTests.cs`'s
  class doc that names SC-1 as "what asks the running system" to name SC-4 (spec §"Why SC-1 and
  SC-2 can be reworked"). Comment-only.
- [ ] **T003 [US1]** Re-subject SC-1 and SC-2 in
  `tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs` onto a planted client
  (`console-scope-probe-{Guid.CreateVersion7():N}`, scopes `sse-identity`, `sse-audience`,
  `sse-groups`, `sse.audit.read`), planted and deleted per fact in `try/finally` via `RealmProbe`
  (plan §D3). Rename SC-1 to describe the subject, not the retired client. The API-answer
  assertions (403 / 200) stay byte-identical in meaning; the `sse.management` absence check stays
  as a **probe-shape control** and its message says so. Update the class doc comment accordingly.
  SC-3, SC-4, SC-8 untouched.
  - **Depends on:** T001.
- [ ] **T004 [P] [US1]** Re-subject `A_caller_without_sse_layouts_write_is_refused_403` in
  `tests/Integration.Tests/LayoutComposition/TileSpanIntegrationTests.cs` onto a planted client
  with the same four scopes, signing in as `admin`; 403 assertion unchanged; doc comment updated.
  - **Depends on:** T001. Disjoint file from T003.
- [ ] **T005 [US1]** Run `ConsoleScopeGrantIntegrationTests` and `TileSpanIntegrationTests`
  against the **unedited** realm on a fresh Keycloak volume. Record the verbatim output: all
  green. This is the characterisation baseline; after T011 these facts must pass without edits.
  - **Depends on:** T002, T003, T004.
- [ ] **T006 [US1]** Commit 1 (plan §5): `test(identity): plant a probe client for the console
  scope facts`. Builds and passes on its own.
  - **Depends on:** T005.

### Red (behaviour-changing)

- [ ] **T007 [P] [US1]** Add `The_retired_console_client_is_not_declared` to
  `tests/Architecture.Tests/ScopeGrantTests.cs` (plan §D4, static).
  - **Depends on:** T006.
- [ ] **T008 [P] [US1]** Add SC-9 `The_retired_client_cannot_mint_a_token` to
  `ConsoleScopeGrantIntegrationTests.cs` (plan §D4, runtime): raw POST via
  `aspire.CreateKeycloakClient()`; assert status and `error` code (expected 401 /
  `invalid_client`); positive control for `management-web` with the same `operator` credentials in
  the same fact. Never assert merely "not 200".
  - **Depends on:** T006. Disjoint file from T007.
- [ ] **T009 [US1]** Run T007 and T008 against the unedited realm; record the verbatim **red**
  output (T008: Keycloak issued a token). Commit 2: `test(identity): red the retired
  smart-sentinel-eye-web client`. Builds; both facts fail.
  - **Depends on:** T007, T008.

## Phase 4b: the retirement (`infra-engineer`)

- [ ] **T010 [US1]** Re-run T009's two facts to confirm the red brief before editing.
  - **Depends on:** T009.
- [ ] **T011 [US1]** Delete the `smart-sentinel-eye-web` object from
  `src/AppHost/Realms/smart-sentinel-eye-realm.json` (plan §3). No other byte changes.
  Commit 3: `fix(apphost): delete the unused smart-sentinel-eye-web client from the realm`.
  - **Depends on:** T010.
- [ ] **T012 [US1]** Delete the Keycloak data volume, boot fresh, run `ScopeGrantTests`,
  `LegacyBundleGrantTests`, `RealmIdentityTests`, `RealmImportMirrorTests`,
  `ConsoleScopeGrantIntegrationTests`, `TileSpanIntegrationTests`,
  `EventTypeRegistryAuthorizationIntegrationTests`, `VariableReadScopeIntegrationTests`. All
  green; T003/T004 facts unmodified since commit 1 (`git diff <commit1> -- <those files>` shows
  only T008's addition). `dotnet build -c Release` clean.
  - **Depends on:** T011.

## Phase 5 (`/verify`)

- [ ] **T013 [US1]** Collect plan §7's evidence; write the verification note on the PR.

## PR body must carry

- The verbatim red (T009) and characterisation-green (T005) output.
- `Latency: N/A — realm config and test fixtures, no leg touched.`
- **Left for a human:** ADR-0080's stale code sketch (issue #2488's own "worth folding in"); the
  lane may not amend an ADR (ADR-0144).
- `Closes #2488`.
