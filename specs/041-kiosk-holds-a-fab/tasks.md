# Tasks: The kiosk holds a fab, and holds only what a kiosk needs

**Feature**: `041-kiosk-holds-a-fab` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1884 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**28 tasks across five phases.** It looked like a two-line change. It is a realm
mapper, an authorization gate, a client switch, a new e2e seeding project, a
reshaped assertion, a convention test that never existed, three corrected
documents, and a verification half no automated check can perform.

**Phase 1 is sequenced first for a reason, and the reason is not tidiness.**
Phase 0 found two blockers between the kiosk and a working wall
([research.md](./research.md) R3, R4). Switching the client before fixing them
produces a kiosk that signs in, lists layouts, opens a wall — and shows **dark
tiles**. A dark tile is exactly the kind of half-success that gets accepted, and
nothing in CI can tell it from a working one.

**Phase 5 is required, not suggested.** `camera-sim` and `scenario-simulator`
both sit inside `if (isRunMode && !isE2ETests)`, so **a Playwright kiosk gets no
video in either direction**. The automated suite proves the kiosk *reaches* a
wall and proves **nothing** about whether the tiles show a picture. Claiming
otherwise would be the same class of error this feature exists to correct.
Spec 040's PR is already open in draft because *its* Phase 5 was not performed,
and this feature is what unblocks it — a Phase 5 skipped here leaves **two**
features resting on unread evidence.

---

## Do not

- **Do not add `sse.management` to `kiosk-web`.** It would make every call
  succeed and defeat the entire second half of the feature. This is the quiet
  widening **FR-005** names by name.
- **Do not relax `WhepAuthValidator`.** It is right to refuse a token it cannot
  attribute. The fix is to make the token attributable (T001), not to accept an
  unattributable one.
- **Do not invent a second grandfather mechanism.** `sse.management` is already
  accepted everywhere through `RequireScopeExtensions.LegacyManagementBundle`.
  T002 uses that same constant; a hand-rolled second rule is how the first one
  (the WHEP gate) came to disagree with the rest of the product.
- **Do not touch `management-web`, `smart-sentinel-eye-web`, or their scope
  request.** The identical trap sits one client over and is documented in
  research R10. Noted, not this change's.
- **Do not restore a `basic`-equivalent realm scope** (research R2), even though
  it is the root cause behind blocker A. It touches every client in the realm.
  One mapper on one client is the narrow fix.
- **Do not change what the kiosk does** — same walls, same overlays, same
  reconnection ladder, same tiles. This changes **who it signs in as** (FR-011).
- **Do not create `data-model.md`.** Nothing persists. A client id and a scope
  set are configuration. An empty model file would be a document asserting
  something that does not exist, which is the failure mode this feature corrects.
- **Do not restart the Keycloak container to pick up a realm edit — recreate
  it.** The realm imports at start-up and an existing volume keeps the old realm.
  Twenty minutes of debugging a client that is still there is the usual price.
- **Do not write `#1884` or `#<blocker-B issue>` in any committed document.**
  A bare mention auto-closes the issue on merge.

---

## Phase 1: Make the intended identity usable

**Goal**: The intended client becomes capable of a kiosk's job. Nothing points at
it yet.

**Both blockers are invisible to CI in both directions.** Fixing them cannot be
proved by the suite, only by Phase 5. That is why they are first: doing them last
would mean discovering in Phase 5 that the switch shipped broken.

- [ ] T001 [US1] Add an `oidc-sub-mapper` protocol mapper to the `kiosk-web` client in `src/AppHost/Realms/smart-sentinel-eye-realm.json` — **blocker A**. The realm supplies its own `clientScopes` array, which *replaces* Keycloak's built-ins rather than adding to them (research R2), so `basic` does not exist and `kiosk-web` holds no scope that emits `sub`. Mirror the mapper already on the `sse.management` client scope (`name: "sub-claim"`, `protocol: "openid-connect"`, `protocolMapper: "oidc-sub-mapper"`). **`sub` is an identity claim, not a permission** — this widens nothing the kiosk may do, so US2 is untouched. Do **not** put it on the shared `sse-groups` scope: that is held by `management-web`, `scenario-simulator` and `stream-distribution-attribution` too.
- [ ] T002 [US1] Change `RequiredScope` in `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs` from `"sse.management"` to `Scope.Sse.Streams.Read`'s value, and accept `sse.management` through `RequireScopeExtensions.LegacyManagementBundle` — **blocker B**. This is the only gate in the product that does not follow the granular-or-grandfathered rule, and as written **no kiosk persona can pass it**, browser or enrolled device. Application must stay ASP.NET-free (ADR-0051), so if referencing `ServiceDefaults` from here is not already possible, keep the two strings local with a comment naming `RequireScopeExtensions` as the rule being mirrored — **do not invent a different rule**. This narrows what is *demanded*; it widens nothing the kiosk *holds*.
- [ ] T003 [US1] Correct the failure message in `src/StreamDistribution/Application/Commands/AuthorizeWhepErrors.cs` — it says *"Bearer token does not grant the sse.management scope."* and becomes wrong the moment T002 lands. Name the scope actually required.
- [ ] T004 [US1] Extend `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepCommandHandlerTests.cs` with four cases: a **kiosk-persona** token (exactly `KeycloakScopeBundles.Kiosk`, no `sse.management`) is **admitted**; a **management** token (`sse.management`, no `sse.streams.read`) is **still admitted** — this is management-web's actual token shape and what a naively-narrowed gate would break; a token with **neither** is refused; a token the validator could not attribute (`Option.None`) is **still** refused. The existing `Authorize_with_a_token_missing_sse_management_returns_Forbidden` has its premise replaced — rename it rather than leaving a test whose name asserts the old rule.
- [ ] T005 [US1] Prove T001 by **minting a token, not by reading the JSON**: import `src/AppHost/Realms/smart-sentinel-eye-realm.json` into a throwaway `quay.io/keycloak/keycloak:26.5` container, mint a token for `kiosk-web` and confirm the **access token** (not just the ID token) now carries `sub` alongside `groups`. Record the payload. This is the same measurement that found the blocker; believing the edit worked is what put it there.

**Checkpoint**: the intended identity can do a kiosk's job. Nothing uses it.

---

## Phase 2: Switch the kiosk and retire the legacy client

**Goal**: The app signs in as the client built for it, and the one it used is
gone.

- [ ] T006 [US1] In `apps/kiosk-web/src/app/auth.ts`, set `client_id: 'kiosk-web'` **and** `scope: 'openid'` — **in this one task, never separately**. Keycloak validates requested scopes against the client's default+optional sets, so asking `kiosk-web` for `sse.management` returns `invalid_scope` and **no token at all** (research R1): a half-switch fails sign-in outright, which is worse than today's failure. Rewrite the doc comment — it currently names the legacy client and *explains* the `sse.management` choice, so both sentences become false. Leave `redirect_uri`, `onSigninCallback` and the production-guard throw exactly as they are.
- [ ] T007 [US4] Delete the `smart-sentinel-eye-kiosk` client object from `src/AppHost/Realms/smart-sentinel-eye-realm.json`. Nothing else in the realm refers to it — no group, no client scope, no service-account role (research R10) — so removing the object is the whole change. **Same file as T001, so not parallel with it.** Spec 009 called it *replaced*; this makes that a fact rather than an annotation.
- [ ] T008 [P] [US4] Correct `.claude/agents/frontend-engineer.md`, which tells a frontend agent the kiosk client is `smart-sentinel-eye-kiosk` with `scope openid sse.management`. A document that hands the next reader the wrong client is how this defect reproduces itself.
- [ ] T009 [P] [US4] Correct the doc comment in `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts` that says *"The hub requires `sse.management` scope"*. `LayoutLifecycleHub` is `[Authorize(Policy = Scope.Sse.Layouts.Read)]`. While there, record what research R6 found: the hub joins one SignalR group **per fab in the `groups` claim**, so a kiosk holding no fab joined none and **has never received a resolved-text or highlight push**.
- [ ] T010 [P] [US4] Sweep the repository for `smart-sentinel-eye-kiosk` and confirm **zero** references outside historical records (**SC-005**). `specs/003-layout-composition/` and `specs/011-frontend-247-resilience/` are historical and stay untouched — FR-008 says *outside historical records*. Record the surviving list in the PR so "zero" is a count someone checked, not a claim.

**Checkpoint**: US1 and US4 are behaviourally complete. Nothing yet proves it.

---

## Phase 3: Make the failure impossible to hide

**Goal**: A check that can tell a working kiosk from a broken one.

This defect survived since the kiosk existed because
`e2e/kiosk-live-updates.spec.ts` accepts *"could not load layouts"* as one of
three passing outcomes. Fixing the kiosk and leaving that assertion would fix
the instance and keep the mechanism — and the mechanism produces the next one.

- [ ] T011 [US3] Create `e2e/support/seed-published-layout.setup.ts` — a Playwright **setup** file that drives management-web at `http://localhost:5173` with the existing `signInAsOperator` from `e2e/support/sign-in.ts`, registers a camera and authors + publishes a **1×1** layout. CI boots with an empty catalogue (`camera-sim` and `scenario-simulator` are excluded from e2e mode), and the kiosk cannot reach a wall without a published one. Mirror the steps `e2e/layouts.spec.ts` already uses. **Check whether a tile requires an overlay** before assuming it does not; if it does, publish one too. Do **not** rely on `layouts.spec.ts` having run — depending on another spec's side effect is the implicit coupling that produces the next silent pass.
- [ ] T012 [US3] Add a `seed` project to `playwright.config.ts` with `testMatch: /.*\.setup\.ts/` and `baseURL: 'http://localhost:5173'`, and give the existing `kiosk` project `dependencies: ['seed']`. Playwright's default `testMatch` only picks up `*.spec.ts` / `*.test.ts`, so a `.setup.ts` file is invisible to the `chromium` and `kiosk` projects — **no `testIgnore` churn**. Leave `chromium`'s `testIgnore: /kiosk-.*\.spec\.ts/` alone.
- [ ] T013 [US3] Create `e2e/support/kiosk-session.ts` holding two helpers: `signInToKiosk(page)`, which drives the Keycloak form as the seeded `operator` and asserts **the picker with at least one layout on it** — never the three-way regex; and `readKioskAccessToken(page)`, which reads the app's own `sessionStorage` entry (`oidc.user:<authority>:kiosk-web`) and returns the decoded access-token payload. Assert **the wall the kiosk can reach**, not the absence of an error: an empty picker raises no error either, and an empty picker is exactly what a fab-less token produces.
- [ ] T014 [US3] Rewrite `e2e/kiosk-live-updates.spec.ts` to import `signInToKiosk` from `e2e/support/kiosk-session.ts`, deleting the local copy whose regex accepts `could not load layouts` as a pass (**FR-007**). The degraded-badge assertions are unchanged — this test's subject is the retry ladder, and FR-011 says the kiosk's behaviour does not change.
- [ ] T015 [US1] Create `e2e/kiosk-shows-a-wall.spec.ts` — named `kiosk-*` so the `kiosk` project's `testMatch` picks it up. Sign in, open the seeded layout, assert `data-testid="layout-grid"` is visible and **at least one** `data-testid="layout-tile"` renders (**SC-001**). Tiles render whether or not video arrives, so state in the file that this proves the kiosk **reaches** a wall and proves **nothing** about a picture — that is Phase 5's job.
- [ ] T016 [US2] Create `e2e/kiosk-identity.spec.ts` asserting the token itself via `readKioskAccessToken`: `groups` contains `/fabs/munich` (**FR-001**, US1 sc.3); `scope` does **not** contain `sse.management` (**FR-003**, **SC-002**); and the `sse.*` entries in `scope` **equal**, as a set, the six in `KeycloakScopeBundles.Kiosk` (**FR-004**, **SC-003**). **The absence is the point.** A check that only confirms the kiosk works passes just as happily with the blunt scope restored — that is exactly how the weakness comes back, and behaviour cannot see it.
- [ ] T017 [US1] Run the kiosk and shared vitest suites and confirm **the existing test files are unmodified** — `git diff` over `apps/kiosk-web/**/*.test.*` and `apps/shared/**/*.test.*` shows nothing (**FR-011**, **SC-007**). Spec 040 did the same. A behavioural claim backed by edited tests is not a claim.

**Checkpoint**: US3 is complete except for proving the check can fail (T026).

---

## Phase 4: Stop the two definitions drifting

**Goal**: The realm's kiosk client and the enrolled-device bundle agree by
construction rather than by two people having written the same list.

- [ ] T018 [US2] Create `tests/Architecture.Tests/KioskScopeParityTests.cs` comparing the `sse.*` entries of the realm's `kiosk-web` `defaultClientScopes` against `KeycloakScopeBundles.Kiosk` as **sets, in both directions** (**FR-009**, **SC-003**). Read both **live**: `Architecture.Tests` already project-references `Identity.Application`, and `LatencyLegRecordTests.ReadConstitution` shows the repo-root walk (upward until `SmartSentinelEye.slnx`). `System.Text.Json` is in the BCL — no new package. Exclude `sse-groups`, which is a claim carrier and not a permission, and say so in the test. A spot check would not notice a scope added to one side, which is the whole failure mode.
- [ ] T019 [P] [US2] Correct the doc comment in `src/Identity/Application/KeycloakAdmin/KeycloakScopeBundles.cs`, which states *"The `ScopeBundleTests` assertion (spec 008 PR F) verifies these strings match the catalogue."* **There is no such file anywhere in the repository** — a doc comment asserting a guard nobody wrote, found the same way as spec 040's, by looking instead of believing. Name `KioskScopeParityTests` and say what it actually checks.
- [ ] T020 [US2] Prove T018 can fail: add a scope to the realm's `kiosk-web`, watch it go red; revert; add one to `KeycloakScopeBundles.Kiosk`, watch it go red; revert. **Both directions**, because a one-directional assertion is half a guard. Record the two failing outputs.

**Checkpoint**: US2 is complete and guarded.

---

## Phase 5: Evidence only a person can give

**Goal**: Observe that a kiosk shows a picture, and that the check can go red.

**This is where both blockers are actually verified.** Everything CI can check
will be green whether or not video works. Follow [quickstart.md](./quickstart.md).

- [ ] T021 **Recreate** the Keycloak container (not restart — an existing volume keeps the old realm) and boot the run-mode stack with `dotnet run --project src/AppHost`. Confirm the realm imported: the `Referenced client scope 'basic' doesn't exist` warnings are **expected** (research R2, and the reason T001 exists); **no** warning may name `smart-sentinel-eye-kiosk`.
- [ ] T022 [US1] Sign into the kiosk at `http://localhost:5174/` as `operator` / `Operator1234` and record all four token claims from quickstart §3 verbatim: `azp` is `kiosk-web`; `groups` is `["/fabs/munich"]`; `scope` is `openid` plus exactly the six kiosk scopes with **no** `sse.management`; **`sub` is present**. A wrong redirect URI fails here rather than at the first API call, so nothing after this point means anything if sign-in does not complete.
- [ ] T023 [US1] Open a wall and **watch a tile for ten seconds** (quickstart §5). Record whether a picture appears. If not, read `stream-distribution`'s log for `POST /streams/authorize`: **401** means the token still has no `sub` (blocker A, T001); **403** means the gate still asks for `sse.management` (blocker B, T002). **This is the only step in the entire feature that can see either blocker.**
- [ ] T024 Confirm management-web at `http://localhost:5173` **still shows video** on a camera detail page. Its token holds `sse.management` and **not** `sse.streams.read`, so it is precisely the case the grandfather clause in T002 exists to keep working — and precisely what a naively-narrowed gate would break. A green unit test is not this observation.
- [ ] T025 [US1] With a wall open, change a system variable referenced by the tile's overlay in management-web and confirm the tile's text updates within about a second without a reload. Research R6: the kiosk has **never** joined a fab SignalR group, so this path has never been exercised. Record the outcome either way.
- [ ] T026 [US3] **Prove the check can fail (SC-004) by causing it.** Point `apps/kiosk-web/src/app/auth.ts` back at `smart-sentinel-eye-kiosk` with `scope: 'openid sse.management'`, restore that client in the realm, recreate Keycloak, and run `pnpm test:e2e --project=kiosk`. It must go **red**, and the failing output goes in the verification note. If it stays green, the assertion still cannot tell working from broken and **nothing has been fixed**. Revert both edits and re-run to green.
- [ ] T027 File a follow-up issue for **blocker B**, because it is bigger than this feature: `KeycloakScopeBundles.Kiosk` carries no `sse.management`, so **no enrolled physical kiosk device has ever been able to watch video**, and constitution §VIII says kiosks hold view-only scopes. Reference it in the PR **without a `#`**. Raise, do not absorb.
- [ ] T028 Write the verification note on the PR stating **which claims rest on Phase 5 and which do not**, and naming any step above that was not performed. The automated suite proves the kiosk reaches a wall and proves nothing about a picture; a PR that implies otherwise repeats the error this feature exists to correct.

---

## Dependencies

```
T001 ─┬─▶ T005                     (mint a token; prove the mapper)
      │
T002 ─┴─▶ T003 ─▶ T004             (the gate, its message, its tests)
      │
      ▼
T006 ─▶ T007                       (switch, then delete — same realm file)
      │
      ├─▶ T008, T009, T010         (three documents, parallel)
      │
      ▼
T011 ─▶ T012 ─▶ T013 ─┬─▶ T014
                      ├─▶ T015
                      └─▶ T016
      │
      ▼
T017                               (the untouched-tests check)

T018 ─▶ T020        T019           (parity, proved; the doc correction is independent)

T021 ─▶ T022 ─▶ T023 ─▶ T024 ─▶ T025 ─▶ T026 ─▶ T027 ─▶ T028
```

**Phase 1 blocks Phase 2 by intent, not by the compiler.** T006 would build and
sign in without T001 or T002. It would also produce a kiosk with dark tiles that
passes every automated check — which is the outcome this ordering exists to
prevent.

**T001 and T007 touch the same file** and are deliberately in different phases:
one makes the intended client work, the other removes the old one. Do not merge
them into a single realm edit — if the switch has to be backed out, the mapper
should stay.

**T012 before T013**, because a session helper that asserts layouts exist is a
failing helper until something seeds one.

**T026 needs everything**, and it needs T022–T025 to have actually happened
rather than to be planned.

---

## Parallel opportunities

- **T008, T009, T010** — three different documents, no shared state.
- **T019** is independent of every other Phase 4 task; the parity test and the
  doc comment it names can be written in either order.
- **T014, T015, T016** — three different e2e files, all downstream of T013.
- **T001/T007 are NOT parallel**: same file. Neither are T002/T003/T004, which
  form a change and its consequences.

---

## Implementation strategy

**MVP is T006 — but only after T001–T005.** Reaching a wall is the visible fix;
the two blockers are what make the wall worth reaching.

**Do Phase 1 completely before starting Phase 2.** Half of it — the mapper
without the gate, or the gate without the mapper — still produces dark tiles, and
the symptom is identical. T005 exists so you know which half is done.

**Do not believe T001 from the diff.** The whole feature exists because a
configuration value was trusted rather than exercised. Mint the token.

**Budget real time for Phase 5.** It needs a recreated Keycloak, a booted stack
with `camera-sim` and `mediamtx`, a published layout and a person at the screen.
It is the only place either blocker can be seen.

---

## Three things most likely to go wrong

1. **The kiosk ships with dark tiles and everything is green.** CI produces no
   video, so both blockers are invisible in both directions: fixed or broken, the
   suite looks identical. The mitigation is ordering (Phase 1 first), T005's
   token measurement, and T023 — and T023 is a person watching a tile, which is
   the kind of task that quietly does not happen. T028 requires the PR to say so
   if it did not.

2. **The WHEP gate gets widened instead of narrowed.** The path of least
   resistance when video fails is to add `sse.management` to `kiosk-web`. Every
   test then passes, the kiosk works, and the entire second half of the feature is
   gone — with SC-002 the only thing that would have noticed, which is exactly why
   T016 asserts an **absence**. FR-005 names this outcome in advance.

3. **The e2e assertion is reshaped but still cannot fail.** Asserting "the picker
   renders" instead of "the picker renders with layouts on it" is a one-word
   difference that restores the original defect: a fab-less token produces an
   empty picker and no error. T026 is the only thing that distinguishes a real
   assertion from a plausible one, and it works by causing the failure rather
   than reasoning about it.

---

## What the automated suite does and does not prove

Stated here so the PR does not have to be trusted to remember it.

| Claim | Proved by |
|---|---|
| The kiosk lists layouts and opens a wall | T015 — automated |
| The kiosk's token carries a fab and not `sse.management` | T016 — automated |
| The two scope sets agree | T018/T020 — automated, both directions |
| The check fails when the kiosk cannot show a wall | T026 — **a person, by causing it** |
| **The tiles show a picture** | T023 — **a person, and nothing else** |
| Management-web still shows video | T024 — **a person**; T004 covers only the unit |
| Live overlay text reaches a kiosk | T025 — **a person** |
