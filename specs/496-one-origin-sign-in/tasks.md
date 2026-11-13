# Tasks: Sign-in at the one origin

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Contract**:
[contracts/one-origin-clients.md](./contracts/one-origin-clients.md) | **Issue**: #3267 (epic #3248, ADR-0192), on Project #13 (added by hand
2026-11-13, checked through the issue's `projectItems`; its status column is unset, #3263's was Todo). No per-task issues.

**Phase 4a colour: red** for every behaviour task, declared by the architect (ADR-0139). A red task is a test written first, run, **observed
failing**, its failure quoted in the PR, then made green. **Declared exceptions, not shortcuts:** the guard row `management-web` × `5173`
(T003) arrives green by design (it closes a coverage hole and changes no behaviour), and the existing fixed-port projects (T008) are guards:
captured green before and passing, **unmodified**, after. A red task that arrives green is a phase-4 failure: stop and report.

**Never touched**: `apps/**`, `src/ApiGateway`, `src/AppHost/*.cs`, the layout hub; the realm's scopes, audiences, grants and attributes; `webOrigins`;
the `http://localhost:*` wildcard (the owner's call, recorded in the spec); every client other than the three; the persona client, which must keep
**no** redirect URIs (`PasswordGrantClientTests`); `signInToKiosk`, `signInAsWallDisplay`, `signInAsOperator` and every existing e2e spec (FR-007).
The realm file starts with a UTF-8 byte-order mark: keep it. Never `git stash` or `git checkout -- .`. **One Aspire stack per machine; stop it
before building.** **Memory is tight on this machine**: one Playwright worker (`--workers=1`), no other heavy process while a stack runs, and
stop the stack between runs. No `Co-Authored-By`. Release build before pushing (analyzers are errors).

**§IV: legs touched none.** Sign-in precedes every live page.

Format: `[ID] [P?] [Story] Description`. `[P]` = disjoint files from every other `[P]` task in the same phase. Stories: US1 operator at the
one origin, US2 kiosk and wall display at their paths, US3 a regression is caught, US4 the fixed ports still work.

## Phase 1 — Baseline and observation (blocks everything)

- [x] **T001** [Setup] Baseline on `496-one-origin-sign-in`, clean tree, no stack, **Release**. Record counts in `verification.md` for
      `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~WallClientDeclarationTests|FullyQualifiedName~RealmIdentityTests|FullyQualifiedName~KioskScopeParityTests|FullyQualifiedName~PasswordGrantClientTests|FullyQualifiedName~RealmAudienceTests"`.
      All green before any edit; otherwise stop and report. (The full `Architecture.Tests` run failed locally earlier because of a nested
      worktree, now removed; do not rely on it, and do not run it for memory's sake.)
- [x] **T002** [Setup] **Observe the defect for all three apps (FR-001, premise 5) on a fresh stack at this pre-change tree.** Boot with
      `PersistentStack=false` (a fresh Keycloak volume is the point; quickstart step 1), wait with `scripts/wait-for-e2e-stack.sh`. For
      `http://localhost:5180/`, `/kiosk/` and `/wall/` run the scratch sign-in probe (kept outside the repo, token and `code` redacted) as `operator`,
      `operator` and `wall-munich`, and record for each the final heading and the blocked `POST …/token`. Also record the counterfactual's "before"
      (quickstart step 4). **If the kiosk or the wall display does not fail, stop: premise 5 is false and the spec is narrowed first.** If they fail
      for a reason other than CORS, record it and stop for the owner. Leave this stack running for T004.

## Phase 2 — Foundational

None. No shared code is needed; the realm edit and the guard are independent files.

## Phase 3 — US3 first: the guard that fails (P2, but it is the red that drives the fix)

**Goal**: removing the one origin from any client fails a check naming it. **Independent test**: T003 red on the current tree.

- [ ] **T003** [P] [US3] (test, **red ×3, green ×1 by declaration**) In `tests/Architecture.Tests/WallClientDeclarationTests.cs` add a
      `ConsoleClient = "management-web"` constant and a `OneOriginPort = "5180"` constant beside `KioskClient` / `WallClient`, and add four rows
      to `A_client_names_the_origin_it_is_served_from`: `(ConsoleClient, "5173")`, and `(ConsoleClient | KioskClient | WallClient, OneOriginPort)`.
      Reuse the existing assertion and failure message unchanged. Run it: the three `5180` rows MUST fail naming the client (quote them); the
      `management-web` × `5173` row MUST pass (state it is the characterisation row). Update the test's own summary comment where it says only two
      clients are covered.
- [ ] **T004** [P] [US1, US2] (test, **red ×3**) Add `e2e/one-origin-sign-in.spec.ts` in the `chromium` project (the name has neither `kiosk-` nor
      `wall-`: the config's patterns are unanchored). `test.use({ baseURL: 'http://localhost:5180' })`; three tests, each with its own inline
      sign-in against **absolute** URLs (no edit to any shared helper): `http://localhost:5180/` as `operator`, asserting the *Cameras* heading;
      `http://localhost:5180/kiosk/` as `operator` and `http://localhost:5180/wall/` as `wall-munich`, each asserting the *Pick a layout*
      heading. Each also asserts that no heading says "Sign-in failed". Run it against T002's still-pre-change stack, `--workers=1`; quote the
      three failures.

## Phase 4 — US1, US2: the fix (P1)

**Goal**: sign-in completes at the one origin for all three apps. **Independent test**: T004 green on a fresh stack.

- [ ] **T005** [US1, US2] Depends on T003, T004. In `src/AppHost/Realms/smart-sentinel-eye-realm.json` add `"http://localhost:5180/*"` to the
      `redirectUris` of `management-web`, `kiosk-web` and `kiosk-wall`, after each client's own-port entry and before the wildcard; keep the BOM
      and the file's formatting. Nothing else in the file changes (`git diff` must show exactly three added lines). T003 goes green.
- [ ] **T006** [P] [US1] Depends on nothing. In `docs/deployment-frontend-env.md`, in the one-origin section, add one bullet: the three clients
      must name the one origin in `redirectUris`; the realm file reaches only a fresh import (ADR-0131), so a persistent stack either drops the
      `keycloak-data` volume or applies the change through the admin API, and a verifier says which they tested (FR-010). `prettier --check`.
- [ ] **T007** [US1, US2] Depends on T005. **Observe green on a fresh stack.** Stop the T002 stack; rebuild (Release) at the fix; boot with
      `PersistentStack=false` and wait for readiness. Run T004's spec, `--workers=1`: green ×3, quote it. Repeat T002's probe for the three apps and
      record the final headings. Then the counterfactual (quickstart step 4): `Origin: http://localhost:5180` gets `Access-Control-Allow-Origin`,
      `Origin: http://localhost:5199` gets none (FR-008, SC-004). If a kiosk or the wall display does not reach the picker once the token request
      is admitted, **diagnose and record the cause, then stop**: a second defect is a new spec, not part of this change.

## Phase 5 — US4: the fixed ports still work (P3)

**Goal**: FR-007. **Independent test**: the existing projects, unmodified.

- [ ] **T008** [P] [US4] (**characterisation guard**) On T007's stack, one worker, run **unmodified** one spec per project that signs in at a fixed
      port: `e2e/cameras.spec.ts` (`chromium`, `:5173`), `e2e/kiosk-shows-a-wall.spec.ts` (`kiosk`, `:5174`) and
      `e2e/wall-outlives-its-session.spec.ts` (`wall`, `:5175`). Green with assertions unmodified; one that has to change is evidence the
      behaviour moved, so stop. State plainly that this is **three specs, not the full suite**; CI runs the rest. If the machine runs low on
      memory, stop and say what was not run rather than retry.

## Phase 6 — Polish, verify, review, PR

- [ ] **T009** [Polish] `dotnet format` check on `WallClientDeclarationTests.cs`, `prettier --check` on the new spec, the docs file and the spec
      documents, `eslint` and `tsc -p e2e/tsconfig.json --noEmit` on the e2e spec, and a Release build of the changed test project. Nothing
      suppressed, nothing narrowed.
- [ ] **T010** [Polish] Phase 5 `/verify`: write `verification.md` from T001, T002, T007, T008 **as observed**, including the redacted before and
      after, the red failures quoted from T003 and T004, the counterfactual headers, and what was **not** run. State the latency leg: none touched.
- [ ] **T011** [Polish] Phase 6: `/code-review`, with `security-reviewer` (**warranted**: the change admits a new origin to a public client's
      token endpoint) and `infra-reviewer` (realm import, CI lane, the unanchored Playwright patterns). Give them the diff range, not the
      conclusions. Findings resolved or accepted in writing in `verification.md`.
- [ ] **T012** [Polish] Phase 7: `gh pr create --base develop` with the template, body citing T003/T004 red output verbatim, `Closes #3267`,
      the persistent-stack consequence, what was not observed, and the Claude Code line. CI green by the manual bucket read, reading the retry
      summary for the new spec (it runs at `:5180` in CI for the first time); merge per the standing authorization. Afterwards verify #3267
      closed and the branch deleted, and that #3247's description still holds.

## Dependencies and order

```text
T001 → T002 ─┬→ T004 ─┐
             │        ├→ T005 → T007 → T008 → T009 → T010 → T011 → T012
T001 ────────┴→ T003 ─┘   T006 [P] ──────────┘
```

T003 and T004 touch disjoint files and can be written in parallel, but T004's red run needs T002's stack up. T005 is a three-line edit and is
last of the code changes; T006 can be written any time. T002 and T007 each boot a stack: **never at the same time**, and stop the first before
the second.

## Implementation strategy

One slice, one PR. The smallest shippable increment is T001–T007: the realm names the one origin and a browser proves all three apps sign in.
T008 is the proof that nothing moved. The wildcard's removal, the end-to-end suite moving onto the one origin (#3247) and production's origin
(#1015) are deliberately not here.
