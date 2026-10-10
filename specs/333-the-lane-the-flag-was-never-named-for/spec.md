# Feature Specification: The lane the flag was never named for

**Feature Branch**: `fix/2297-e2e-ci-topology` (cut from `origin/develop` at `b97afeaa`)

**Created**: 2026-10-10

**Status**: Phase 1 gate passed. §4's `[NEEDS CLARIFICATION]` is resolved — route B, decided by the
user 2026-10-10. The decision recorded on the issue could not be implemented as worded; route B
delivers it instead — see §2.

**Input**: Issue [#2297](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2297):
*The e2e CI job runs in dev mode — persistent lifetimes, data volumes, pgAdmin and a fixed host
port*. Found by the 2026-09-13 infra review.

**Decision on the issue (user, 2026-10-10):** "set `E2ETests=true` in the e2e CI job and accept
whatever topology change that causes (no persistent volumes, no pgAdmin, real two-replica gateway
topology, no fixed mediamtx port). This matches what the flag is for, and closes #2283's
single-replica gap as a side effect. If this reveals topology-dependent e2e assumptions that need
fixing alongside, that's in scope for the same PR."

**Spec number.** 333. `origin/develop` tops out at 329. 330 is claimed by
`origin/fix/2286-mqtt-publish-scope-enforcement` (`specs/330-the-scope-the-broker-never-read`), 331
by `origin/fix/2672-stream-watcher-log-verbosity` (`specs/331-the-probe-that-reports-nothing-changed`).
332 is left to the in-flight `D:/Github/sse-2764` worktree (`fix/2764-leading-underscore-analyzer`,
same base, no spec directory yet) so the two architects cannot race for it. All remote branches and
all six worktrees checked 2026-10-10. **Re-check before opening the PR** (memory: *spec number:
origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0068 and ADR-0103 (the integration fixture boots
"in E2ETests mode" — the flag's actual owner), ADR-0108 (Playwright e2e runs against a live
**run-mode** `aspire run` stack), ADR-0153 (one instance per service — why there is no
two-replica branch any more), ADR-0111 (Scenario Simulator dev-only; the `ScenarioSimulator`
switch precedent, #2013), ADR-0024 (Aspire is the composition root), ADR-0139 / ADR-0144
(phase-4a colour), ADR-0037 (phases).

---

## 1. The issue's premise, re-checked against `develop` (2026-10-10)

The issue was filed against a 2026-09-13 tree. Line numbers have moved and one of its claims no
longer holds.

| Issue says | Today (`src/AppHost/AppHost.cs` at `b97afeaa`) |
|---|---|
| `isRunMode && !isE2ETests` at `:95, 118, 129, 209, 253, 303, 547` | Persistence blocks at **:107** (postgres: Persistent + data volume + **pgAdmin**), **:130** (rabbitmq), **:170** (keycloak), **:297** (mosquitto), **:332** (azurite). **:253** mediamtx: Persistent + raw `--publish 8189:8189/udp,tcp` + `MTX_WEBRTCADDITIONALHOSTS=127.0.0.1`. Plus three the issue did not list: **:696** gateway `RateLimiting__PermitLimit=6000`, **:706** the four Vite apps, **:838** the simulator (already off in CI via `ScenarioSimulator=false`). |
| "one gateway replica instead of two" (replica branch at `:537`) | **Gone.** #2283 closed (commits `a32b88a0`…`2af0ff9e`) by removing `WithReplicas(2)` under ADR-0153 clause 1. `AppHostReplicaCountTests` pins **one instance for every service in both the run-mode and the fixture shape**. There is no two-replica topology in any lane to switch to. |
| `E2ETests` is the e2e lane's flag | **It is the integration fixture's flag** — ADR-0068 ("boots the AppHost in E2ETests mode"), `AspireFixture.cs:317`, and `AppHostE2ESwitchTests`'s own doc: *"`E2ETests` means 'this is the integration fixture' and also removes the Vite apps; the end-to-end job needs those … so it cannot use `E2ETests`."* The name predates the Playwright suite (ADR-0108). |

**Was leaving it off an oversight (#2268 question)?** No. It was decided, and recorded, three
times: #2013 / `AppHostE2ESwitchTests.The_simulator_argument_leaves_the_web_apps_and_the_fixture_video_in_place`
("the standing proof that this change is not the one the issue proposed"); spec 076 (`AppHost.cs:226-242`);
spec 232 (`AppHost.cs:685-690`, "AppHostE2ESwitchTests pins that CI passes no E2ETests"). #2268 itself
is about the readiness gate's coverage and says nothing about the flag beyond cross-referencing this
issue. What *was* never revisited is the narrower point the issue makes: the e2e lane inherits the
dev lane's **persistence** because persistence is gated on the same `!isE2ETests` as the web apps.

## 2. What `E2ETests=true` in `ci.yml` would actually do

Every item below is read from the code; none is speculative except where marked.

| # | Effect | Consequence for the e2e job |
|---|---|---|
| B1 | `:706` — management-web, kiosk-web, kiosk-wall, management-cameras **not composed** | `scripts/wait-for-e2e-stack.sh` fails hard at "management-web never served on :5173"; `playwright.config.ts` has every project's `baseURL` on 5173/5174/5175. **All four shards red before a single test runs.** |
| B2 | `:696` — gateway reverts to the 100/min production `PermitLimit` | Spec 232 measured ~246 gateway requests/min from **one** four-tile wall at rest. Widespread 429s across the suite. |
| B3 | `:435` — `/streams/authorize` drops to 50 per 10 s | Integration-lane ceiling sized for 27 known xUnit consumers; every Playwright tile open is an authorize call. 429s on video-bearing specs. |
| B4 | `:624` — `EventIngestion__IngestWrite__Concurrency=1` | Any concurrent `/events/manual` or `/events/webhook` write from the suite gets 429 (spec 223's blast-radius note). |
| B5 | `:604`/`:612` — 3 s audit retention sweep, ingest-breakdown stamps on | Behaviour the e2e suite has never run against. Not proven harmful; not proven harmless. |
| B6 | `:253` — no `8189` port map, no `MTX_WEBRTCADDITIONALHOSTS` | The browser's ICE has no host-reachable candidate. `click-to-first-frame.spec.ts`, `kiosk-shows-*-over-video.spec.ts` and the wall specs count decoded frames. **Unverified guess:** on a Linux runner the host can route to Docker bridge IPs, so ICE *might* still connect in CI; it would not on Docker Desktop. |
| B7 | Persistence blocks off, pgAdmin gone | The intended effect. No e2e spec, support file or script depends on pgAdmin, on data surviving a run, or on a container restart (grep of `e2e/`, `playwright.config.ts`, `scripts/wait-for-e2e-stack.sh`; the script's `^postgres` filter already excludes pgAdmin). **Harmless.** |
| B8 | Replicas | No change — one instance either way (§1). #2283's gap is not closed by this; it was closed by ADR-0153 instead. |
| B9 | Guards | `AppHostGatewayRateBudgetTests`, `AppHostWebAppHostingTests`, `AppHostE2ESwitchTests` all encode "CI's e2e boot passes no E2ETests"; their doc comments become false. |

**Making the literal flip work** means un-gating B1, B2 and B6 from `E2ETests` and moving B3–B5 under
a new integration-only flag — i.e. splitting `E2ETests` into two flags and renaming its meaning
across ~15 test classes and the fixture. That is a redesign of the lane model the flag encodes, not
"turn it on and fix what breaks", and it arrives at the same composition as option B below with a
much larger diff.

## 3. What the issue actually needs

Re-reading the issue's own *Done looks like*, it offered two routes; the second was chosen on the
premise that the flag means "e2e" and that a two-replica path exists. Neither holds. The first route —
*"gate persistence on something other than `!isE2ETests`"* — delivers every concrete cost the issue
lists except the fixed port, and touches nothing the Playwright suite relies on.

| Cost named in the issue | Option B (recommended) | Literal flip |
|---|---|---|
| Persistent containers + named volumes survive the PID kill | **Fixed** | Fixed |
| pgAdmin in CI | **Fixed** | Fixed |
| Fixed host port 8189 collides between concurrent stacks | **Kept** — it is how the browser's ICE reaches MediaMTX (B6) | Removed, video specs at risk |
| Rate-limit tests at one replica (#2283) | N/A — closed by ADR-0153 | N/A — same |
| e2e suite still runs | **Yes** | **No** (B1) |

## 4. Decision (resolved 2026-10-10)

> **Q1. The decision as worded breaks every e2e shard (B1) and its stated side-benefit no longer
> exists (B8). Which route?**
>
> - A — literal flip. Set `E2ETests=true` and split the flag so the e2e lane keeps the web apps,
>   the 6000/min budget and the ICE port while losing persistence; move the fixture-only overrides
>   (B3–B5) under a new integration-only flag. Large; rewrites the meaning of a flag ~15 guard classes
>   and ADR-0068 depend on; arguably needs an ADR because it redefines the lane model.
> - B — orthogonal persistence switch. New AppHost switch `PersistentStack`,
>   default on (a developer's bare `aspire run` is unchanged, same convention as `ScenarioSimulator`),
>   `ci.yml`'s e2e boot passes `PersistentStack=false`. Gates every `Persistent` lifetime, every data
>   volume and pgAdmin. Keeps the web apps, the gateway budget and the 8189 ICE map. Small, no ADR.
> - C — close as superseded. Record on the issue that the replica claim is resolved by ADR-0153
>   and the residual (volumes outliving the run on a non-ephemeral host) is accepted because every CI
>   runner is ephemeral.
>
> **Q2 (only if B).** The 8189 fixed port stays, so two stacks on one host still collide. Accept that
> as the residual (memory *one machine, one Aspire stack* already makes concurrent
> stacks unsupported on a dev box), or file a follow-up to test whether a Linux runner can drop the
> port map (B6's unverified guess)?

**Decision (user, 2026-10-10): Q1 = route B. Q2 = accept the 8189 port residual as a known,
documented gap (not pursuing a Linux-runner follow-up).**

`plan.md` and `tasks.md` are written for **B** and implement it.

---

## User Scenarios & Testing (route B)

### User Story 1 — The e2e job's stack leaves nothing behind (Priority: P1)

As a maintainer running CI on any host — ephemeral GitHub runner, self-hosted runner, or a dev box
reproducing a shard — I want the e2e job's stack to compose no persistent container, no named data
volume and no pgAdmin, so that killing the AppHost ends the stack and the next boot starts from
nothing.

**Why this priority**: it is the issue's concrete cost; everything else in the issue is resolved
elsewhere (§1, §3).

**Independent Test**: build the AppHost model with exactly `ci.yml`'s e2e boot arguments and read
the composed annotations (no containers started); separately, boot the stack with those arguments,
stop it, and observe `docker ps -a` / `docker volume ls`.

**Acceptance Scenarios**:

1. **Given** the AppHost composed with `ScenarioSimulator=false PersistentStack=false` (the e2e
   boot's switches), **When** its resources are read, **Then** no resource carries a
   `ContainerLifetime.Persistent` lifetime annotation, no container carries a `Volume` mount, and no
   `pgadmin` resource exists.
2. **Given** the same composition, **When** its resources are read, **Then** `management-web`,
   `kiosk-web`, `kiosk-wall`, `management-cameras` and `fixture-video` are present, `api-gateway`
   carries `RateLimiting__PermitLimit=6000`, and `mediamtx` still carries the `--publish 8189:8189/udp`
   and `/tcp` runtime args and `MTX_WEBRTCADDITIONALHOSTS=127.0.0.1` — the e2e lane loses only
   persistence.
3. **Given** the AppHost composed with no switches (a developer's `aspire run`), **When** its
   resources are read, **Then** postgres, rabbitmq, keycloak, mosquitto, azurite and mediamtx are
   still `Persistent`, the five data volumes are still mounted and `pgadmin` is present — the dev
   lane is unchanged.
4. **Given** `.github/workflows/ci.yml`, **When** the single e2e boot line is read, **Then**
   `PersistentStack=false` appears in the command ahead of the `>` redirection (a misspelt or
   misplaced switch fails open to persistence, so the workflow text itself is guarded).
5. **Given** an unparseable value (`PersistentStack=nope`), **When** the model is composed, **Then**
   it is persistent — absent or unparseable means a developer's stack, the same fail-open convention
   `ScenarioSimulator` uses (this is the bad-request case; it is asserted, not left implied).
6. **Given** the e2e job on the PR's own CI run, **When** all four `e2e-shards` run, **Then** the
   stack readiness gate passes and the suite's verdict is no worse than `develop`'s at the same base.

*Conflict / auth scenarios:* not applicable — no endpoint, command or authorisation surface changes.

### Edge Cases

- **Integration fixture (`E2ETests=true`).** Already non-persistent; must stay byte-identical in
  composition. The new switch is evaluated *inside* the existing `isRunMode && !isE2ETests` gate, so
  it cannot reach the fixture.
- **camera-sim** (`AppHost.cs:850`) carries its own unconditional `Persistent`. Not composed in CI
  (`ScenarioSimulator=false`), but `PersistentStack=false` should mean *no* persistent container
  anywhere — gated too, so a developer running `aspire run -- PersistentStack=false` with the
  simulator on gets what the switch says.
- **Azurite** is configured inside the `RunAsEmulator` callback, not on a resource builder held in
  scope; the gate goes inside that callback, as today.
- **mediamtx's block is split**: lifetime moves under the persistence gate; the ICE port map and
  additional-hosts env stay under `isRunMode && !isE2ETests` (they are a run-mode networking need,
  not persistence).
- **The fixed host port 8189** remains — residual accepted unless Q2 says otherwise.
- **Graceful stop.** `ci.yml`'s `kill` sends SIGTERM; DCP removes session-lifetime containers on
  shutdown. Whether that completes before the runner tears down is not this spec's concern on an
  ephemeral runner; the guarantee is that nothing is *created* persistent.

## Requirements (route B)

- **FR-001**: `AppHost.cs` MUST read a `PersistentStack` configuration value with the same parse
  shape as `ScenarioSimulator` (absent or unparseable ⇒ `true`).
- **FR-002**: Every `WithLifetime(ContainerLifetime.Persistent)`, every `WithDataVolume()` /
  `WithVolume(...)`, and `WithPgAdmin()` in `AppHost.cs` MUST be composed only when
  `isRunMode && !isE2ETests && isPersistentStack` — postgres, rabbitmq, keycloak, mosquitto, azurite,
  mediamtx (lifetime only), camera-sim (lifetime only).
- **FR-003**: The mediamtx ICE port map and `MTX_WEBRTCADDITIONALHOSTS`, the four Vite apps, the
  gateway's 6000/min budget, `fixture-video` and every `isE2ETests`-only override MUST compose
  exactly as today in every lane.
- **FR-004**: `ci.yml`'s e2e boot line MUST pass `PersistentStack=false` ahead of the redirection.
- **FR-005**: The header comment (`AppHost.cs:12-13`) and each persistence block's comment MUST say
  which lane it is for (the issue's *"say at each branch which lane it is for"*). Doc comments in
  `AppHostE2ESwitchTests` / `AppHostReplicaCountTests` / `AppHostGatewayRateBudgetTests` that
  describe the e2e boot's switches MUST stay true (they say "passes no E2ETests" — still true).
  No issue or spec numbers in new code comments (house rule).
- **FR-006**: No production service, no realm, no e2e spec and no script changes.

## Success Criteria (definition of done, route B)

- **SC-001 (red)**: the new model-composition tests for scenarios 1, 4 and 5's e2e half fail against
  `develop` — quoted verbatim in the PR.
- **SC-002 (green)**: the same tests pass after the change; scenarios 2, 3 and 5's dev half pass both
  before and after (characterisation — they must not need editing).
- **SC-003 (live)**: the PR's own CI run shows all four `e2e-shards` past "Wait for the stack to be
  ready" and the suite no worse than `develop`; the `apphost.log` or stack-status report names no
  `pgadmin` resource.
- **SC-004 (observed)**: a local boot with the e2e switches, then stop, leaves `docker volume ls` and
  `docker ps -a` with nothing from the stack — quoted in the verification note.
- **SC-005**: `Release` build, analyzers and the integration shard holding the new tests green; the
  new test class has its `shard-N.filter` entry.

## Latency budget impact

N/A — composition of dev/CI containers only. No leg of constitution §IV is touched; the e2e lane's
media path (mediamtx ICE map) is deliberately left unchanged so the render-leg figure the e2e job
publishes (spec 225) is measured on the same topology as before.

## Assumptions

- `ContainerLifetimeAnnotation` and `ContainerMountAnnotation` on the composed model are what DCP
  acts on, so reading them is reading what runs (same footing as `AppHostReplicaCountTests` and
  `E2ETests_argument_leaves_postgres_without_a_data_volume`). Phase 5's SC-004 checks the running
  system once (memory: *guards that read the design artefact*).
- The switch name `PersistentStack` is a proposal; the user may prefer another.
