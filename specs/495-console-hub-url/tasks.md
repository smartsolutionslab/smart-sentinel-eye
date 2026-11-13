# Tasks: The operator console's live updates reach the hub

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Contract**:
[contracts/console-hub-wiring.md](./contracts/console-hub-wiring.md) | **Issue**: #3263 (epic #3248), on Project #13 (Todo; checked
2026-11-13 through the issue's `projectItems`). No per-task issues.

**Phase 4a colour: red** for every behaviour task, declared by the architect (ADR-0139). A red task is a test written first, run,
**observed failing**, its failure quoted in the PR, then made green. A test that arrives green where red is declared is a phase-4 failure:
stop and report, or prove it by counterfactual and say so. The **guards** (T010, T011) are the opposite colour: captured green before and
passing, unmodified, after.

**Never touched**: `BoundaryTests.cs` (if a requirement cannot be met without editing it, stop and amend ADR-0192, which the lane may not
do); `@microsoft/signalr`; `LayoutHubCors.cs`, the hub class and the `MapHub` line; `src/ApiGateway`; the kiosks' wiring and
`apps/kiosk-web`; Keycloak and the realm file; `apps/management-web` and `apps/shared` **source** (the plan changes none). Never `git stash`
or `git checkout -- .`. One Aspire stack per machine, and **stop it before building**. No `Co-Authored-By`. New tests go into existing
classes so no shard-filter entry is needed; the one new e2e spec is registered with the e2e shard guard in its own commit. Release build
before pushing (analyzers are errors).

**§IV: legs touched none.** The console opens the same direct connection the kiosks do; the kiosks' connection is unchanged (T010).

Format: `[ID] [P?] [Story] Description`. `[P]` = disjoint files from every other `[P]` task in the same phase. Stories: US1 live scene
changes reach the wall page, US2 one URL for the person, US3 kiosk and wall unaffected.

## Phase 1 — Baseline and observation (blocks everything)

- [ ] **T001** [Setup] Baseline on `495-console-hub-url`, a clean tree, no stack running. Run and record counts in `verification.md`:
      `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostCompositionCharacterisationTests` (all lanes), `LayoutHubCorsTests`
      (`Category=FixtureLogic`, `tests/Integration.Tests`), and `dotnet test tests/Architecture.Tests` (including
      `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`). All must be green before any edit; if one is not, stop and report.
- [ ] **T002** [Setup] **Observe the defect on the running stack (FR-001, premise 6).** The stack left running from the merged 494 worktree
      holds the machine's one Aspire slot and its binaries; **ask the user to stop it first** (a shared stack is not mine to kill). Then boot
      this branch's stack, sign in, open a wall's detail page at `http://localhost:5180/` and at `http://localhost:5173/`, and record for
      each: where `/hubs/layouts/negotiate` goes, the status, and that no WebSocket opens (quickstart steps 1–3, "Before"). Redact the token.
      Write it into `verification.md` as observed. If the page already connects, **stop**: the premise is false, the issue closes with
      the evidence, and the spec is withdrawn.

## Phase 2 — Foundational

None. The one helper the change needs, `LayoutHubUrl(...)`, already exists in `WebApplications.cs`.

## Phase 3 — US1: live scene changes reach the operator's wall page (P1)

**Goal**: the wall detail page's hub connection establishes at both origins and a scene switch re-reads the wall. **Independent test**: T005
green on the stack, plus T003 and T004 green in-process.

- [ ] **T003** [P] [US1] (test, **red**) In `tests/Integration.Tests/AppHostOneOriginTests.cs` add
      `The_console_reaches_the_hub_on_layout_composition_directly`, shaped like the kiosks' theory in
      `AppHostKioskOneOriginTests`: `management-web`'s raw environment contains `VITE_LAYOUT_HUB_URL`, a `ReferenceExpression` whose
      `Format` is `"{0}" + LayoutLifecycleHub.Path` over one `EndpointReference` to `layout-composition`. Also assert it does **not** carry
      `VITE_LAYOUT_HUB_ORIGIN` (the console has no hub proxy, and gains none). Run it; quote the failure (`ShouldContainKey` on an absent key).
- [ ] **T004** [P] [US1] (test, **red**) In `tests/Integration.Tests/AppHostKioskOneOriginTests.cs` edit
      `Layout_composition_admits_the_kiosk_the_wall_and_the_gateway_origins_from_endpoint_references` to expect
      `["api-gateway", "kiosk-wall", "kiosk-web", "management-web"]`, and rename it to say "…and the console's origins". This is a
      behaviour-changing feature, not a refactor, so editing this assertion is the red step; the PR says so. Run it; quote the failure.
- [ ] **T005** [US1] (test, **red**) Add `e2e/wall-detail-live-update.spec.ts`: through the API create a wall with two scenes (as the other
      e2e specs create and delete their own data), open its detail page at the origin the suite captured (the gateway), switch the scene
      with `POST /walls/{id}/switch` (it takes the wall's `If-Match` version), and expect the page to show the new scene within 2 s without a
      reload. Register it with the e2e shard guard in the same commit. Run it against the T002 stack; quote the failure. **If seeding proves
      too heavy**, drop the spec, say so in `verification.md`, and let T002 and T012 carry the end-to-end evidence (plan, Risks).
- [ ] **T006** [US1] Depends on T003, T004. In `src/AppHost/WebApplications.cs`: (a) `AddManagementWeb` gains
      `.WithEnvironment("VITE_LAYOUT_HUB_URL", LayoutHubUrl(layoutComposition))`, with **no** `WithReference`, `VITE_LAYOUT_HUB_ORIGIN` or
      `WaitFor` (plan decision 2); (b) the block after `WithOneOriginUi` adds
      `.WithEnvironment("LayoutHub__Cors__AllowedOrigins__3", managementWeb.GetEndpoint("http"))`. T003 and T004 go green.
- [ ] **T007** [US1] (test, **red then green**) Depends on T006's shape, written **before** T006 is run. The three
      `tests/Integration.Tests/AppHostComposition/{Developer,EndToEndJob,Unparseable}.snapshot.txt` are literals with **no regeneration
      path** (the test class says so). Hand-edit each: the new `env VITE_LAYOUT_HUB_URL=…` line on `management-web`, the new
      `env LayoutHub__Cors__AllowedOrigins__3=…` line on `layout-composition`, and **every annotation index `[N]` after each insertion**, in
      all three files. Run `AppHostCompositionCharacterisationTests` against the unedited AppHost to see it red, then after T006 green;
      quote both. A snapshot line that moves is evidence the composition moved, and here it is meant to: say so in the PR.
- [ ] **T008** [P] [US1] Correct the stale prose (FR-008). `src/AppHost/WebApplications.cs`: the comment at lines ~14–27 that says
      "management-web's vite.config.ts reads no environment and has no such proxy", and the one at ~87 ("called cross-origin from the
      kiosk, the wall and the one origin"). `docs/deployment-frontend-env.md`: the sentence that lists the origins the AppHost sets, and one
      line saying the console needs `VITE_LAYOUT_HUB_URL` in a production build exactly as the kiosks do (FR-007; wiring deferred to #1015).
      Keep each comment to a short *why*, per CLAUDE.md; a spec number does not go in the source.
- [ ] **T009** [US1] Depends on T005, T006. Run T005 green on the rebooted stack and repeat T002's two page loads for "After": the negotiate
      request goes to `layout-composition`, answers 200, a WebSocket opens, at `:5180` and at `:5173`. Record in `verification.md`.

## Phase 4 — US2: the person still uses one URL (P1)

**Goal**: nothing asks for, shows or requires a hub address. **Independent test**: T010's walk.

- [ ] **T010** [US2] Using only `http://localhost:5180/`, walk every console page including the wall detail page (quickstart steps 2 and
      4). Confirm no page prompts for or displays another address and the live update works. Record in `verification.md` (SC-002). This is
      an observation, not a test, because "nothing asks" has no assertion to write.

## Phase 5 — US3: the kiosk and the wall display are unaffected (P1)

**Goal**: §IV and FR-006. **Independent test**: the guards, unmodified.

- [ ] **T011** [P] [US3] (**characterisation guard**) Re-run, **unmodified**: the kiosks' theory
      `Each_kiosk_instance_reaches_the_hub_on_layout_composition_directly`, `LayoutHubCorsTests` (including
      `A_preflight_from_an_origin_that_is_not_configured_gets_no_allow_origin`, which is SC-004's test), `dotnet test tests/Architecture.Tests`
      (`BoundaryTests` untouched), the `kiosk` and `wall` Playwright projects, and `apps/kiosk-web` vitest (one at a time). All green with
      assertions unmodified; an assertion that has to change is evidence the behaviour moved, so stop.
- [ ] **T012** [US3] Open `/kiosk/` and `/wall/` on the stack and confirm each connects to the hub as before (quickstart step 6). Then do
      SC-004 once on the live service: a preflight from an origin that is not listed gets no `Access-Control-Allow-Origin`.

## Phase 6 — Polish, verify, review, PR

- [ ] **T013** [Polish] Format and Release analyzers clean: `dotnet format` on the touched projects, `dotnet build -c Release` (stack
      stopped), `prettier --check` on the new spec (eslint clean is not prettier clean), the e2e shard guard script. Nothing suppressed,
      nothing narrowed.
- [ ] **T014** [Polish] Phase 5 `/verify`: write the verification note (`verification.md`) from T001, T002, T009, T010, T012 as observed,
      including the redacted before/after requests and the red failures quoted from T003, T004, T005, T007. State the latency leg: none
      touched. Record anything that did not behave as the plan expected.
- [ ] **T015** [Polish] File the follow-up the plan deferred: a new issue "management-web: tell the operator when the layout hub
      connection cannot be established" (FR-009), body linking #3263 and spec 495. Add it to Project #13 only if it is to be worked; else
      leave it unboarded.
- [ ] **T016** [Polish] Phase 6 `/code-review`, with `backend-reviewer` and `infra-reviewer` (read-only); `/security-review` is warranted
      because the change widens an origin list that allows credentials. Findings resolved or accepted in writing in `verification.md`.
- [ ] **T017** [Polish] Phase 7: `gh pr create --base develop` with the template, body citing T003/T004/T005/T007 red output verbatim,
      `Closes #3263`, and the Claude Code line. CI green by the manual four-bucket read; merge per the standing authorization. Afterwards
      check the issue closed and the branch deleted.

## Dependencies and order

```text
T001 → T002 ─┬→ T003 ─┐
             ├→ T004 ─┼→ T006 → T007(green) ─┐
             └→ T005 ─┘         T008 [P]      ├→ T009 → T010 → T012 → T013 → T014 → T016 → T017
                                T011 [P] ─────┘                         T015 [P]
```

T003, T004, T005 and T008 touch disjoint files and can be written in parallel; T005 needs the stack up and T002 needs the user. T006 is
one AppHost file and is last of the code changes.

## Implementation strategy

One slice, one PR. The smallest shippable increment is T001–T009: the console's hub connects. T010–T012 are observations that discharge
the spec's success criteria. FR-009 (the operator is told when it cannot connect) is deliberately not here; T015 files it.
