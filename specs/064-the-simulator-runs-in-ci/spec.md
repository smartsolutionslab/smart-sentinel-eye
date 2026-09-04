# Feature Specification: The simulator runs in CI

**Feature Branch**: `fix/2013-the-simulator-runs-in-ci`

**Created**: 2026-09-04

**Status**: Draft — phases 1–3 complete, phase 4a not started

**Issues**: #2013

**Lane**: ADR-0144 autonomous. `agent:ready` present, `agent:blocked` absent.

**Input (#2013)**: "`camera-sim` and `scenario-simulator` sit inside
`if (isRunMode && !isE2ETests)` in the AppHost. […] The e2e job boots the stack
with plain `dotnet run`, which is run mode, so the guard is **true** and both
resources start in CI. […] Decide which is intended — the simulator genuinely
dev-only (set the flag in CI), or acknowledged in CI (amend ADR-0111 and correct
the comments) — rather than leaving the guard, the ADR and the comments
disagreeing."

---

## The fork that decides the approach — answered first, with commands

The issue offers two resolutions and the lane can only take one of them:
implementing ADR-0111's recorded decision is in scope; amending ADR-0111 is
forbidden outright (ADR-0144). So the branch had to be chosen on evidence, and
the deciding question was: **do the e2e tests actually depend on the simulator's
video and its 23 seeded cameras?**

They do not. But the answer that matters more is one the issue does not
contain: **its first option cannot be built as worded.** Both findings below,
then the ruling.

### Finding A — no e2e test reads anything the simulator produces

```
$ grep -rn "camera-sim\|station-\|rolling-mill\|sim-loop\|8554" e2e/ apps/shared/src
e2e/camera-detail.spec.ts:203:       * **What this does NOT prove: that a picture appears.** `camera-sim` and
e2e/kiosk-shows-a-wall.spec.ts:15:   * **What this does NOT prove: that the tiles show a picture.** `camera-sim`,
e2e/support/live-video-wall.ts:43:     process.env['E2E_FIXTURE_VIDEO_RTSP_URL'] ?? 'rtsp://fixture-video:8554/loop';
e2e/support/seed-published-layout.setup.ts:8:  * `camera-sim` and `scenario-simulator` both sit inside
apps/shared/src/observability/kioskLatency.test.ts:9:  * `camera-sim`, `scenario-simulator` and the ICE host-publishing all sit inside
```

**Four of the five hits are comments.** The fifth is spec 056's
`FIXTURE_VIDEO_RTSP_URL`, and it points at **`fixture-video`** — a *different*
container, stood up by spec 056 precisely so a fixture would not race the
simulator's worker (ADR-0138 records that rejection: *"Rejected on the race,
not on the video"*).

Every e2e spec seeds the data it asserts on, through the UI, in its own setup:

- `e2e/support/seed-published-layout.setup.ts` registers `Kiosk Seed Cam
  <stamp>` and publishes `Kiosk Seed Wall <stamp>`.
- `e2e/layouts.spec.ts` registers `E2E Cam <stamp>` and publishes `E2E Overlay
  <stamp>` before authoring the layout.
- `e2e/camera-detail.spec.ts` calls `registerCamera(page)`.
- `e2e/cameras.spec.ts` registers `E2E Cam <stamp>` / `E2E Fab <stamp>`.

No assertion counts cameras. The only `toBeGreaterThan` over a listed
collection is `e2e/rules.spec.ts:52`, which counts **fabs** (realm data, not
catalogue data), and `e2e/kiosk-shows-a-wall.spec.ts:27`, which counts tiles on
the wall its own setup published.

**So the 23 simulator cameras are read by nothing, and its video is consumed by
nothing.** Removing them from CI breaks no assertion that exists today.

### Finding B — `E2ETests=true` cannot be set on the CI boot, and the issue does not know this

The issue's first option is *"set the flag in CI"*. That flag does not gate the
simulator. It gates a **set** of resources, and three of them are the ones the
Playwright suite drives:

`src/AppHost/AppHost.cs:435` — the same `if (isRunMode && !isE2ETests)` block
adds `management-web` (:5173), `kiosk-web` (:5174) and `kiosk-wall` (:5175),
under the comment *"Skipped in test mode so the integration suite doesn't start
two Node dev servers."*

`scripts/wait-for-e2e-stack.sh` then hard-fails on exactly those ports:

```sh
if [ "$web_up" != "1" ]; then
  echo "::error::management-web never served on :5173"
  exit 1
fi
...
for port in 5174 5175; do
  ...
  echo "::error::nothing served on :$port"
```

And `src/AppHost/AppHost.cs:146` puts `fixture-video` behind the same guard,
with a comment that states the dependency in as many words:

> **Gated exactly as `camera-sim` is**, and the Playwright stack still gets it:
> `E2ETests` is set by the *integration* fixture (`AspireFixture`) and by
> `AppHostE2ESwitchTests`, but **not** by the end-to-end stack boot […] So this
> is present where a browser needs a picture and absent where nothing consumes
> one.

**Setting `E2ETests=true` on the e2e boot would not tidy the simulator away. It
would delete the three front ends under test, fail the wait script before
Playwright started, and remove the video source spec 056 exists to provide.**
It is not a fix with a cost; it is not a fix.

### The ruling — the dichotomy is false, and there is a third option

`E2ETests` does not mean *"not CI"*. It means *"this is the integration fixture,
not a stack a browser or a human will drive"* — which is why it removes data
volumes, pgAdmin, gateway replicas and the Vite apps, and adds
`WaitForCompletion(migrations)`. The simulator was gated on it because that flag
was the nearest available switch, not because it was the right one.

ADR-0111 **already decided** the simulator is dev-only:

> Gated `isRunMode && !isE2ETests` (off under E2ETests/CI/prod — zero impact).
> […] All dev-only, so prod/CI are untouched.

That decision is intact and is not in question. What is wrong is the mechanism
chosen to deliver it. **Giving the simulator its own switch makes ADR-0111 true
as written**, without amending a word of it, and without touching the web apps
or `fixture-video`, whose gate is about a different concern and is correct.

- Not a new architectural decision → not an ADR → **the run is not blocked**.
- ADR-0111 needs **no amendment**. Its Consequences paragraph becomes true
  rather than being rewritten.

**This is the smallest change that makes the guard, the ADR and the comments
agree**, which is what the issue asked for.

---

## What the record actually says, and how much of it is wrong

The issue names three comments. There are **six** places, and the guard's own
comment is one of them.

| Location | What it claims | True after this change |
|---|---|---|
| `src/AppHost/AppHost.cs:508` | *"so CI/E2E/prod never see it"* | yes |
| `e2e/kiosk-shows-a-wall.spec.ts:15-18` | *"so a Playwright kiosk gets no video"* | yes — but see below |
| `e2e/support/seed-published-layout.setup.ts:8-9` | *"so CI boots on an empty catalogue"* | yes |
| `apps/shared/src/observability/kioskLatency.test.ts:9-11` | *"CI has no video"* | yes — but see below |
| `e2e/camera-detail.spec.ts:203-205` | *"a Playwright run produces no video at all"* | **no** |
| `e2e/layouts.spec.ts:22` | *"CI runs on a fresh, empty DB"* | yes |

**Two of these do not become true, they become differently untrue**, and that
is the trap this feature must not walk into. Since spec 056, CI *does* have
video — from `fixture-video`, which stays. A comment saying *"CI has no video"*
is false today because of the simulator, and would still be false tomorrow
because of `fixture-video`. Each comment must be rewritten to say what its own
test is actually blind to, not to repeat a stack-wide claim.

`e2e/camera-detail.spec.ts` is the sharpest case: its camera is registered at an
address nothing serves. That spec gets no picture because **it never asked for
one** — a true and durable reason that does not mention the simulator at all.

`docs/design/scenario-simulator-m2.md:37,53,518` and
`src/AppHost/Resources/README.md:12` restate the guard. They are design
documents describing the intended shape; after this change they describe it
correctly and need only the guard expression updated where it is quoted
verbatim.

**ADR-0138 already recorded this defect** (lines 122–135) as an open finding,
including the correction that a first draft's *"never set to `true` anywhere"*
was itself false. Nothing in ADR-0138 needs changing; this feature closes the
finding it raised.

---

## User Scenarios & Testing

### User Story 1 — the CI stack boots without the simulator (Priority: P1)

**As** an engineer reading a comment in the e2e suite, **I want** the AppHost's
gate, ADR-0111 and the comments to say the same thing, **so that** what I am
told about my own CI is true.

This is the whole feature and it ships in one slice: one switch, one CI
argument, one set of corrected comments. There is no second story.

**Why it is P1 and alone:** the comments are only true *because* of the code
change. Landing them separately would commit a comment that is false until its
successor arrives — the defect being fixed, delivered twice.

#### Acceptance scenarios

**The happy path — CI**

```gherkin
Given the AppHost is started in run mode
  And the argument "ScenarioSimulator=false" is passed
When the application model is built
Then it contains no resource named "camera-sim"
  And it contains no resource named "scenario-simulator"
  And it still contains "management-web", "kiosk-web" and "kiosk-wall"
  And it still contains "fixture-video"
```

**The happy path — a developer**

```gherkin
Given the AppHost is started in run mode
  And no "ScenarioSimulator" argument is passed
When the application model is built
Then it contains "camera-sim" and "scenario-simulator"
```

`aspire run` is unchanged. A developer who has never heard of this switch gets
exactly the stack they got yesterday — the point of defaulting on.

**The conflicting switch**

```gherkin
Given the AppHost is started with "E2ETests=true"
  And no "ScenarioSimulator" argument is passed
When the application model is built
Then it contains no "camera-sim" and no "scenario-simulator"
```

The existing `E2ETests` gate keeps its meaning and keeps removing the simulator.
The two switches are `AND`ed; neither weakens the other. This is the scenario
`AppHostE2ESwitchTests` already asserts, and it must still pass **unmodified** —
if it needs editing, the change went further than intended.

**The bad-request equivalent — a malformed switch value**

```gherkin
Given the AppHost is started in run mode
  And the argument "ScenarioSimulator=yes" is passed
When the application model is built
Then it contains "camera-sim" and "scenario-simulator"
```

**This is a deliberate fail-open, and it is a real risk, stated rather than
hidden.** An unparseable value means a developer's stack, because absence and
nonsense are indistinguishable to `bool.TryParse` and the dev default must
survive a typo. The cost is that a typo *in `ci.yml`* would silently restore the
exact bug being fixed. That is what makes the otherwise-weak `ci.yml`-reading
test (T003) earn its place: it is the only thing standing between a mistyped
argument and a silent regression.

**Auth**

Not applicable, and for a structural reason rather than an oversight: nothing
here crosses a trust boundary. The change is an AppHost composition switch and a
CI workflow argument; no HTTP endpoint, no scope, no token, no persisted state.
The one adjacent security-shaped fact is that the simulator holds a Keycloak
client secret (`ScenarioSimulator__Runtime__ClientSecret`, a dev-only parameter)
and this change means CI stops minting a token with it — strictly a reduction.

---

## Independent end-to-end test procedure

Runnable by someone who has read nothing above.

1. Build the solution.
2. Boot the stack the way CI does, with the new argument:
   `dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj -- ScenarioSimulator=false`
3. In the Aspire dashboard, confirm **`camera-sim` and `scenario-simulator` are
   absent**, and that `management-web`, `kiosk-web`, `kiosk-wall`,
   `fixture-video` and `mediamtx` are all present and healthy.
4. Get an operator token and read the catalogue:
   `GET {gateway}/camera-catalog/cameras`. **Expect zero cameras**, not 23.
   (The pre-change control: boot without the argument and see 23 at
   `rtsp://camera-sim:8554/...`.)
5. Run the full Playwright suite against that stack: `pnpm test:e2e`.
   **Every spec that passed before must still pass** — including
   `kiosk-shows-a-label-over-video.spec.ts`, which is the one that genuinely
   needs a picture and so proves `fixture-video` survived the change.
6. Re-boot with no argument (`dotnet run --project ...`) and confirm
   `camera-sim` and `scenario-simulator` are back. A developer's stack is
   unchanged.

Step 5 is the load-bearing one. Findings A and B are a reading of the source;
step 5 is the only thing that can contradict them, and it is where a spec that
passes for a reason nobody wrote down would surface.

---

## Locked technology choices

Nothing new is chosen. Every mechanism already exists in this repository:

| Concern | Choice | Where it already exists |
|---|---|---|
| Composition root | Aspire AppHost | ADR-0024/0025 |
| Switch mechanism | bare `key=value` command-line argument read via `builder.Configuration` | `AspireFixture.cs:85`, `AppHostE2ESwitchTests.cs:25` pass `E2ETests=true` this way |
| Naming | `ScenarioSimulator` — no abbreviation | ADR-0091 |
| Model-only assertion | `DistributedApplicationTestingBuilder.CreateAsync` | `AppHostE2ESwitchTests` |
| Test stack | xUnit + Shouldly | ADR-0052 |

The switch is passed as a **command-line argument, not an environment
variable**, deliberately: `E2ETests` already travels that way, and one mechanism
proven to reach `builder.Configuration` beats two that each need proving.

---

## Latency budget impact (constitution §IV)

**N/A — no leg is touched, and no leg's evidence is touched.**

Nothing on the event → overlay path changes. The AppHost gate is composition,
not runtime; the CI workflow is not a fab.

Two adjacent statements are worth making because §IV has twice drifted by
inference:

- **No §IV row changes state.** Not to *measured*, not to *built*. This feature
  measures nothing and observes nothing on the SLO path.
- **The figures already recorded are unaffected.** Every latency figure in this
  repository was read against a **developer's run-mode stack**, which still has
  the simulator (the specs 045 and 046 verification notes both say so
  explicitly). This change alters CI only. Any future measurement taken against
  CI must state that the simulator was absent — which after this change is
  simply true, rather than believed-true-and-wasn't.

Indirectly, CI loses a MediaMTX container, a .NET worker that waits on eight
resources, and 23 RTSP path provisions during the warm-up window the wait script
already stretches to 12.5 minutes. That should make the e2e job faster and less
contended. **It is not claimed as a result** — no one has measured it, and a
speed-up asserted without a figure is the same error as a measured leg nobody
read.

---

## Scope rulings

**1. The unmeasured race is closed here, not filed separately.**

The issue raises a third thing outside its own suggested resolution: nothing
waits for the simulator to finish seeding, so *"whether a simulator camera
exists when a test looks is a race nobody has measured."*

**It belongs in this spec, because this change removes the racer.** With
`scenario-simulator` absent from CI, there is no worker seeding a catalogue
concurrently with a Playwright setup, and nothing left to race. Filing a
separate issue would file it against a condition that no longer holds.

Recorded explicitly because the reasoning is contingent: **had the fork resolved
the other way** — acknowledging the simulator in CI — the race would have been
the larger of the two problems and would have needed its own issue and its own
wait predicate (the shape spec 062 built for `migrations`). It is dissolved by
the branch taken, not by being unimportant.

**2. The developer stack's race is untouched and stays open.**

A developer's `aspire run` still starts the simulator with nothing waiting on
its seeding, and `docs/design/scenario-simulator-m2.md` still plans M2 on top of
it. This feature makes no claim about that. It is not a defect anyone has hit
and is not in scope.

**3. `fixture-video`, `management-web`, `kiosk-web`, `kiosk-wall`, pgAdmin, data
volumes and gateway replicas keep the `E2ETests` gate.**

Their gate is correct: it separates the integration fixture from a stack a
browser drives. Only the simulator was mis-gated. Widening the change to
"rationalise the guards" would be a refactor riding along with a fix, which
CLAUDE.md forbids.

**4. ADR-0111 is not edited.** Not one word. If a reviewer concludes it *should*
be edited, that is a decision the autonomous lane may not make (ADR-0144) and
the work stops.

**5. M2 of ADR-0111 is not started, resumed or considered.**

## What is explicitly not being built

- No wait-for-seeding predicate, in CI or in dev.
- No removal or weakening of the `E2ETests` switch or its guard test.
- No change to any e2e spec's assertions — comments only.
- No new ADR, no ADR amendment, no constitution amendment.
- No claim that the e2e job got faster.
