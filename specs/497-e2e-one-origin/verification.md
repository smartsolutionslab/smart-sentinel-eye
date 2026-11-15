# Verification: The end-to-end suite on the one origin

Observed, in the order the tasks ran, per stage. Nothing here is inferred.

## T001 — baseline (2026-11-15, branch `497-e2e-one-origin`, clean tree, 0 behind `origin/develop`)

**(a) Which spec runs where**, `pnpm exec playwright test --list --project=<p>`, one saved list per project (kept outside the repo, sorted, for the
`diff` in T013, T025 and T031):

| Project    | Tests listed |
| ---------- | ------------ |
| `chromium` | 107          |
| `seed`     | 3            |
| `kiosk`    | 32           |
| `wall`     | 20           |
| `cleanup`  | 3            |

**(b) The guards**: `pnpm test:guards`: `tests 350, pass 350, fail 0, cancelled 0, skipped 0` (94 s). It already contains the readiness script's own
cases (migrations, one-shot resources, the `:5176` and `:5177` remotes), each tested with a stub `curl`, which is the idiom the warm-up follows.

**(c) The retry baseline (SC-005)**, `develop`'s last green CI run, `43514387662` at `01f2421f` (the merge of #3270):

| Shard | Passed | Flaky |
| ----- | ------ | ----- |
| 1     | 42     | 0     |
| 2     | 32     | 0     |
| 3     | 48     | 0     |
| 4     | 39     | 1     |

**161 passed and 1 flaky**, the flaky test being `[kiosk] › e2e/kiosk-paints-a-reordered-overlay.spec.ts:39` (it failed once and passed on retry;
1 failed mark in the logs). The #3270 PR run before it had 0 flaky of 162. So the baseline is **0 to 2 flaky per run**, the same kiosk specs
recurring (`kiosk-credential-refused`, `kiosk-paints-a-reordered-overlay`).

**(d) The origin counts, re-measured**, against what the plan states:

| Where                                           | Plan said                       | Measured                                                                                                                                                                                                                                              |
| ----------------------------------------------- | ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `e2e/` (`localhost:517x` or `:5180`)            | 25 lines in 18 files            | **25 lines in 18 files** ✓                                                                                                                                                                                                                            |
| `playwright.config.ts`                          | 5 lines                         | **5 lines** ✓                                                                                                                                                                                                                                         |
| a bare port literal in `e2e/` (no `localhost:`) | 1 (`kiosk-identity-refused:56`) | **1** ✓                                                                                                                                                                                                                                               |
| `scripts/wait-for-e2e-stack.sh`                 | 3                               | **3 `localhost:517x` literals, but 11 lines that mention a port** (variables, messages, a `for port in 5174 5175` loop). The plan's "3" understated what the guard will see; the file is a `PERMANENT` entry as a whole, so this changes no decision. |

Two plan corrections found while doing this, both written into the plan and tasks before any code:

1. **The warm-up is a section of `wait-for-e2e-stack.sh`, tested in `wait-for-e2e-stack.test.mjs` with a stub `curl`**, not a separate
   `warm-one-origin.sh` with a stub HTTP server. The existing test file already has the matching `:5176` and `:5177` cases in exactly that idiom; it
   adds no file, and the new section must keep the existing cases green (T007).
2. **The `PERMANENT` entry for the readiness script covers its port mentions as a whole file** (reason: it names the stack's ports: the upstream dev
   servers the gateway proxies, and the one origin it warms).

## Stage 1: the guard, red first (T002 to T004)

**T002, red run 1** (`scripts/e2e-origins.test.mjs` written before the scanner): `ERR_MODULE_NOT_FOUND: Cannot find module '…/scripts/e2e-origins.mjs'`,
`tests 1, pass 0, fail 1`. Proves nothing on its own; quoted to show run 1 happened (the repo's idiom, as in `node-types-alignment`).

**T003, the scanner with both allow-lists empty**: the 11 fixture cases pass and the **real-tree test is red on today's tree**, listing **87**
offending lines: 48 in `e2e/`, 9 in `playwright.config.ts`, 12 in `scripts/wait-for-e2e-stack.sh`, 18 in application source. That is more than the
plan's 31 for two reasons, one a decision and one a finding:

- **Decision: a comment-only line is prose, not a hard-coded origin.** 37 of the 87 were port numbers in comments ("Kiosk app (`:5174`)", "next free
  port after 5173/5174/5175"). Forcing 20+ prose rewrites would inflate every stage for no safety. A fixture pins it
  (`a port named only in a comment is prose, not a hard-coded origin`): **red first** (it reported lines 1 to 4, the comment lines), then green after the
  change; code with a trailing comment is still scanned.
- **Finding: application source has more exceptions than the plan named**: the four apps' `vite.config.ts` dev ports **and two** no-window
  fallbacks (`management-web` and `kiosk-web` `auth.ts`), not one.

With comment-only lines skipped the real-tree red is **50** lines: 26 in `e2e/`, 5 in the config (**31**, exactly the plan's figure), 11 in the readiness
script, 8 in application source. The 31, `file:line  port`:

```text
e2e/console-live-scene-switch.spec.ts:9 5173            e2e/kiosk-touch-target.spec.ts:20 5174
e2e/console-live-scene-switch.spec.ts:40 5173           e2e/one-origin-sign-in.spec.ts:5 5180
e2e/kiosk-comes-back.spec.ts:81 5174                    e2e/rule-switches-a-wall.spec.ts:51 5173
e2e/kiosk-identity-herd.spec.ts:27 5174                 e2e/rule-switches-a-wall.spec.ts:77 5174
e2e/kiosk-identity-refused.spec.ts:56 5174  (bare '5174') e2e/spanning-wall.spec.ts:119 5174
e2e/kiosk-overlay-tile-view-matches-the-wall.spec.ts:93 5173   e2e/wall-changes-its-scene.spec.ts:69 5173
e2e/kiosk-paints-a-reordered-overlay.spec.ts:61 5173    e2e/wall-changes-its-scene.spec.ts:95 5174
e2e/kiosk-reconciliation.spec.ts:66 5173                e2e/wall-changes-its-scene.spec.ts:145 5174
e2e/kiosk-shows-a-label-over-video.spec.ts:1300 5173    e2e/wall-outlives-its-session.spec.ts:105 5175
e2e/kiosk-shows-shapes-over-video.spec.ts:48 5173       e2e/wall-survives-a-lockout.spec.ts:80 5175
e2e/kiosk-shows-two-labels-over-video.spec.ts:52 5173   e2e/wall-survives-a-process-death.spec.ts:29 5175
playwright.config.ts:22 5173   :64 5173   :106 5173     e2e/wall-withdrawal.spec.ts:79, :80, :107, :108 5175
playwright.config.ts:70 5174   :82 5175
```

By port: 13 × `5173`, 9 × `5174`, 8 × `5175`, 1 × `5180`.

**T004, the allow-lists filled.** `PERMANENT` (7, each with a reason): the readiness script; the four apps' `vite.config.ts` (a dev server's own port is
its definition); the two `auth.ts` no-window fallbacks. `MIGRATION` (13, one per file and port, with its stage): the 8 stage 2 entries (`5174`) and the 5
stage 3 entries (`5175`). The real-tree test now names **only the 14 stage 1 references**: 13 × `5173` and the `5180` literal in
`one-origin-sign-in.spec.ts`. The plan and the contract said 3 permanent entries; it is 7, in the same categories.

## Stage 1: the warm-up, red then green (T005 to T007)

**T005, red** (`scripts/wait-for-e2e-stack.test.mjs`, two cases added in the file's existing stub-`curl` idiom, before the section existed):

```text
✖ a stack whose one origin never serves /wall/ is not reported ready (spec 497)
    the gate opened over a stack whose one origin never served /wall/       (status 0: the script never asks for /wall/)
✖ a stack whose one origin serves its three UI paths is reported ready, and the readiness step names each (spec 497)
    AssertionError: expected the readiness output to name 5180/             (the output never mentions the one origin)
```

**T006, green.** `wait-for-e2e-stack.sh` gains `ONE_ORIGIN="http://localhost:5180"` and, after the kiosk waits (the gateway proxies those), a loop that
requests `/`, `/kiosk/` and `/wall/` through it (up to 60 tries of 5 s, `--max-time 20` each, because the first proxied load runs Vite's first-request
transforms), printing each path as it answers and failing with `::error::the one origin never served <path> (<url>)`. `bash -n` is clean and the file
has no CRs. Both new cases pass.

**T007: the whole file, with the section present**: `tests 10, pass 10, fail 0`: the 8 existing cases (migrations, one-shot resources, the status
report, the `:5176` and `:5177` remotes) and the 2 new ones. **No existing stub needed a change**: the default `curl` stub answers success for any URL, so the
existing cases satisfy the new section without being loosened.

## Stage 1: the move (T008 to T013)

**T008 to T010.** `e2e/support/origins.ts` (`ONE_ORIGIN`, `KIOSK_PATH`, `WALL_PATH`, no environment override). `playwright.config.ts` imports it and the default,
`seed` and `cleanup` base URLs use it; the `kiosk` and `wall` projects keep their ports (their `MIGRATION` entries). The `:5173` literal in 9 spec files
(10 occurrences) and the `:5180` literal in `one-origin-sign-in.spec.ts` became `ONE_ORIGIN`, by a script that **checks each file's occurrence count first and
fails on any surprise**. `git diff` shows only added import lines and replaced literals: no assertion, timeout or step touched. `testMatch`, `testIgnore`,
`dependencies` and `teardown` are unchanged. `prettier`, `eslint --max-warnings 0` and `tsc -p e2e/tsconfig.json --noEmit` are clean (the one prettier
warning, `e2e/support/render-leg.test.mjs`, is a file this change does not touch, and `.mjs` is outside CI's format glob).

**T011, the read-through** (a single context opening two apps): none. Each touched spec drives the console in the default `page` (the console project) or
in an explicit separate `browser.newContext`, and the kiosk likewise: `console-live-scene-switch` (page and `otherContext`, both console), `rule-switches-a-wall` and
`wall-changes-its-scene` (`page` console, a separate `kioskContext`), and the six `kiosk-*` specs (`page` kiosk, a separate `operatorContext`, `adminContext`
or `management` for the console). `kiosk-reconciliation`'s `admin.goto('/')` is the console's root and stays correct. `one-origin-sign-in`'s three tests each
get their own page. A separate `browser.newContext` has its own storage, so the three apps sharing one origin's storage (spec premise 11) cannot cross here.

**T012, the guard goes green, and the ratchet works in both directions.** The real-tree test passes (13 of 13): the 14 stage 1 references are gone. Then,
on a scratch edit restored afterwards (`git diff` empty):

```text
delete a live MIGRATION entry (e2e/spanning-wall.spec.ts, 5174)  -> e2e/spanning-wall.spec.ts:119  5174  const kioskContext = …localhost:5174…
add an entry for an occurrence that is gone (…console-live-scene-switch…, 5173) -> e2e/console-live-scene-switch.spec.ts  5173  stale migration entry (stage 1): remove it
```

**T013, which spec runs where is unchanged (FR-008, SC-002).** `playwright test --list --project=<p>` before and after, compared by project, file and title:
`chromium` 107, `seed` 3, `kiosk` 32, `wall` 20, `cleanup` 3: **identical in all five**, and Playwright's own `Total: N tests in M files` lines are equal
(107 in 26, 32 in 23, 20 in 12). The raw lists differ only by `file:line:col`, which moved by one line where an import was added.

## Stage 1: the small local run (T014): **not discharged**

A fresh stack was booted (`dotnet run --project src/AppHost -c Release --no-build -- ScenarioSimulator=false PersistentStack=false`, detached) and
`scripts/wait-for-e2e-stack.sh` started, with ~6.4 GB free memory and a browser holding another ~8.7 GB. **The harness stopped the readiness wait for
critical memory before it printed anything**, with the stack mid-boot (7 containers up, free memory down to 3.7 GB and falling). I did not restart it
(the harness's instruction) and **stopped the stack** (AppHost, services, containers, Vite and Playwright browsers: none left; free memory back to ~5.6 GB).

So **not observed locally**: the new warm-up's output against a real stack (the three "Waiting for the one origin to serve …" lines), `e2e/cameras.spec.ts`
and `e2e/console-live-scene-switch.spec.ts` at the one origin. What stands in its place: the readiness script's new section is proved by the stub-`curl`
cases (T005 to T007: red, then green, with the existing 8 cases intact), and `e2e/one-origin-sign-in.spec.ts` (green in CI since #3270, 6 passed on fresh
stacks locally in spec 496) already shows sign-in and first screens at the one origin. **Stage 1's CI run is therefore the first time the console suite
runs at `:5180`**, as the plan said it would be.

## Stage 1: summary of what was observed and what was not (T015 to T017)

**Latency leg touched: none.** The suite and CI scripts changed; no product code, and the hub, Keycloak and media stay direct.

**T015, the fixed ports are untouched by construction (FR-007, FR-011):** the diff against `origin/develop` is confined to `e2e/`, `scripts/`,
`playwright.config.ts`, `specs/` and `.specify/`; **no** file under `src/`, `apps/`, `tests/` or `deploy/` is touched, so nothing that serves or configures a
fixed port moved. **T016:** `prettier --check` (every touched `.ts`, `.mjs`, `.md`), `eslint e2e playwright.config.ts --max-warnings 0`,
`tsc -p e2e/tsconfig.json --noEmit` clean; `pnpm test:guards`: **365 passed, 0 failed** (350 before, plus 13 scanner cases and 2 warm-up cases).

| Spec requirement (stage 1)                                          | Status                                                                                                                      |
| ------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| FR-004 the guard fails on a hard-coded origin, naming file and line | **Done, red first**: 87 lines before the comment rule, 50 after, the 31 in the suite and config listed; red on today's tree |
| FR-005 a written, reviewed exception list                           | **Done**: 7 `PERMANENT` entries each with a reason; 13 `MIGRATION` entries by stage; stale entries fail                     |
| FR-006 the readiness step warms the three paths and names a failure | **Done, red first**, with the stub `curl` cases (10 of 10 pass); **not seen against a real stack** (T014)                   |
| FR-002 the console projects run at the one origin                   | **Moved** (config default, `seed`, `cleanup`, 9 spec files); **not run at `:5180` locally**; CI is the first run            |
| FR-008 no spec dropped or re-routed                                 | **Observed**: per-project lists identical by project, file and title; Playwright's own totals equal                         |
| FR-007 no weakened, deleted or skipped spec                         | **Observed**: the diff is added imports and replaced origin literals only                                                   |
| FR-010 retries seen, not absorbed                                   | **Pending**: the PR run's retry summary against the baseline (0 to 2 flaky per run)                                         |

**Not observed:** anything of the console suite running at `:5180` (T014: the local stack run was stopped by the harness for memory; the owner chose to let
CI be the first proof), so stage 1's CI run is **the first time every console spec runs through the gateway and the proxied remotes**, and the eleven
specs that derive the gateway's origin from a page request (plan decision 10) run against the gateway's bare routes for the first time.

## Stage 1: review (T018) and dispositions

Two read-only reviewers (`frontend-reviewer`, `infra-reviewer`) were given the diff range and the intent, not this note's conclusions. They ran no stack
and no Playwright. **Neither found a blocker.** The frontend reviewer confirmed the key fact for the move: the console was already built with
`VITE_API_GATEWAY_URL="/api"` and `devProxy.ts` copies the gateway's routing, so every API path is identical at `:5173` and `:5180`; no helper or
request matcher depends on the page's origin or port; no single context mixes apps; the eleven specs that capture the gateway's origin parse only
`.origin` and build bare `/<context>/...` URLs the gateway serves.

| #   | Finding                                                                                                                                                                                                                                  | Disposition                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| --- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| I1  | **Should-fix.** Two readiness assertions could not fail: `5180/` also matched the `/kiosk/` and `/wall/` lines, and the failure test was satisfied by the progress line, not `::error::`                                                 | **Fixed**, and proved. The healthy case now asserts one anchored progress line per path; the failure case asserts the `::error::` line. **Mutation 1** (drop `/` from the loop): the healthy case goes red, `expected the readiness output to ask for http://localhost:5180/`. **Mutation 2** (the error no longer names the path): the failure case goes red. Both restored.                                                                                                                                                                       |
| I2  | **Should-fix.** The guard can pass while a violation exists: a new `:5174` line in a listed file; a whole-file exemption for a one-line reason (`auth.ts`)                                                                               | **Fixed**, red first (6 cases red, then green: 17 of 17). `MIGRATION` entries now carry a `count` (a new occurrence is a violation, a lower count is stale until lowered); `PERMANENT` takes a `match` and the two `auth.ts` entries are pinned to the no-window fallback line; a `PERMANENT` entry whose file names no port is stale. Probes on the real files in memory: an extra `:5174` line in `spanning-wall` is reported; a `fetch` in `kiosk-web`'s `auth.ts` is reported; the listed spec fully moved with its entry still there is stale. |
| I3  | **Should-fix.** The warm-up comment claims what the code does not: `curl` fetches HTML only, so no module is pre-transformed (the direct probes already answered in ~15 ms); and the stated bound (~5 min) was really 60 x 25 s = 25 min | **Fixed.** It is a **reachability gate**, and the script comment, spec premise 8 and user story 4, plan decision 8 and research R6 now say so (each noting the correction). Bound: 30 tries of at most 10 s plus a sleep, about 7 minutes, and a refused connection fails at once.                                                                                                                                                                                                                                                                  |
| I4  | Blind spots in the pattern (a bash brace range, `'5173-5177'`, a port before a dot, a `#` private field, `/* x */ code`)                                                                                                                 | **Documented** as known limits in the contract; none hides an occurrence in today's tree (the reviewer listed every tracked file with a port).                                                                                                                                                                                                                                                                                                                                                                                                      |
| I5  | The scope is narrower than "a CI script": only `scripts/*.sh`                                                                                                                                                                            | **Fixed**: `scripts/*.{sh,mjs}`, with the guard's own entry (`this guard names the ports it forbids`). No other script names a port.                                                                                                                                                                                                                                                                                                                                                                                                                |
| I6  | Comments that now say the console is on `:5173` survive, because the guard skips comment lines                                                                                                                                           | **Fixed** in the lines next to the change: `wall-changes-its-scene`, `rule-switches-a-wall`, `kiosk-overlay-tile-view-matches-the-wall`, `seed-published-layout.setup.ts`, `playwright.config.ts`. `spanning-wall.spec.ts:26` is a stage 2 file and is left to stage 2.                                                                                                                                                                                                                                                                             |
| I7  | After this stage no e2e spec opens the console at `:5173`: that developer path is no longer tested by CI                                                                                                                                 | **Accepted and recorded.** See the lane below.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| I8  | `curl -fsS` without `-L` treats a 3xx as success                                                                                                                                                                                         | **Accepted.** All three paths end in `/`, so Vite answers 200 and the bare-prefix redirect is not hit; a redirecting stack would pass without being looked at, which a reachability gate can live with.                                                                                                                                                                                                                                                                                                                                             |
| F1  | Stale `:5173` comments next to touched lines                                                                                                                                                                                             | Fixed (I6).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| F2  | The readiness message "serving after ~Ns" under-reports when a `curl` times out                                                                                                                                                          | **Fixed**: it prints elapsed `$SECONDS`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| F3  | The span test's printed submit round trip (`kiosk-shows-a-label-over-video`) loses one hop: the operator's PUT now goes browser to gateway, not browser to Vite proxy to gateway                                                         | **In the PR body.** No code change: the persisted render-leg record (`overlay_draw`) is measured kiosk-side and is unaffected; the printed `[span]` figures are not exactly comparable with earlier runs.                                                                                                                                                                                                                                                                                                                                           |

**A lane that has become untested (FR-011, recorded because the guard cannot see it).** After stage 1 no e2e spec opens the console at `:5173`. Its Vite
proxy (`/api`, `/remotes/*`, the bare REST contexts) is covered only by `devProxy.test.ts`, which checks the proxy table, not live traffic. FR-011 promises
developers that the fixed ports keep working; that promise now holds **by construction** (no product, AppHost, realm or `apps/` file changed) and **not by CI**.
The same will be true of `:5174` after stage 2 and `:5175` after stage 3.

After the review fixes: `prettier`, `eslint --max-warnings 0` and `tsc -p e2e/tsconfig.json --noEmit` are clean, and `pnpm test:guards` passes
**369 of 369** (350 baseline; the guard has 17 cases and the readiness file 10).

## Stage 2: the kiosk (`:5174`) (branch `497-stage-2-kiosk`, cut from `develop` at `f6aae96b`, stage 1 merged in #3271)

`develop`'s own CI run on stage 1's merge commit was still in progress when this stage was started (the e2e shards were running). The branch was cut and the static
work done in the meantime; **the PR is not opened until that run is green.**

**T020, red 1 (the premise, no stack)**: `new URL('/', 'http://localhost:5180/kiosk/').href` prints `http://localhost:5180/`: a leading `/` throws away
`/kiosk/`, so a helper that does `goto('/')` against a kiosk base lands on the **console** at the one origin.

**T020, red 2 (the guard)**: with this stage's 8 `MIGRATION` entries deleted (commit `a4adb408`), the real-tree test is red and names exactly the nine `:5174` lines:

```text
e2e/kiosk-comes-back.spec.ts:81         e2e/rule-switches-a-wall.spec.ts:78      e2e/wall-changes-its-scene.spec.ts:96
e2e/kiosk-identity-herd.spec.ts:27      e2e/spanning-wall.spec.ts:119            e2e/wall-changes-its-scene.spec.ts:146
e2e/kiosk-identity-refused.spec.ts:56   (bare '5174')                            e2e/kiosk-touch-target.spec.ts:20
playwright.config.ts:72
```

**T021, red 3 (a kiosk spec at the one origin with the helper unchanged, on a booted stack): NOT RUN.** No stack was booted for this stage: a full stack has run
this machine out of memory three times, and for stage 1 the owner chose to let CI be the first proof. This stage rests on red 1 (deterministic, above) and on
CI, and **the gap is named**: the unchanged-helper failure was predicted from URL resolution, not observed in a browser.

**T022, the move.** `playwright.config.ts`: the `kiosk` project's base URL is `ONE_ORIGIN`. `support/kiosk-session.ts`: `signInToKiosk` opens `KIOSK_PATH`;
`kiosk-comes-back` (the rebooted context's base and its `goto`) and `kiosk-touch-target` (its context's base and `goto`) open `KIOSK_PATH`; the `:5174`
contexts in `kiosk-identity-herd`, `rule-switches-a-wall`, `spanning-wall` and `wall-changes-its-scene` (x2) became `ONE_ORIGIN` (they open the kiosk through
`signInToKiosk`). Only origins and paths changed. `kiosk-reconciliation`'s `admin.goto('/')` is the **console's** root and stays. The comments that still
said the kiosk was on `:5174` were updated (the kiosk half only: the wall display's `:5175` is still true until stage 3).

**T023, the one edited assertion (declared), and it changed again after review.** `kiosk-identity-refused.spec.ts`, in the test "shows no credential prompt
anywhere, and is not handed to the provider":

```diff
-    expect(new URL(page.url()).port, 'a refused screen must not be redirected to the provider').toBe('5174');
+    expect(providerOrigin, 'the intercepted request must name the provider').not.toBe('');
+    const here = new URL(page.url());
+    expect(here.origin, 'a refused screen must not be redirected to the provider').not.toBe(providerOrigin);
+    expect(here.pathname.startsWith(KIOSK_PATH), 'and it stays on the kiosk, not the console').toBe(true);
```

(`providerOrigin` is captured in the route handler the test already has for `**/protocol/openid-connect/token`.) **How it got here.** The first version of this stage
compared `page.url().origin` to `ONE_ORIGIN`. Both reviewers pointed out that it was weaker than the port check in one way and fragile in another: `:5174` meant "the kiosk dev
server", whereas `ONE_ORIGIN` means "the kiosk **or the console**", so a refused screen that fell back to the console's root would pass it; and it would pass
vacuously if ADR-0192's _deferred_ "Keycloak behind the gateway" ever put the provider's login page on the one origin. The final form asserts against the provider's real
origin (so it holds wherever the provider is hosted) **and** that the page is still under `/kiosk/` (which also rules out the console). The property asserted is unchanged:
a refused screen is not on the identity provider's page.

**What is and is not claimed.** The old port comparison could not hold at the one origin, where the kiosk and the console share a port: that is reasoning from the
configuration, **not an observation**, because red 3 (T021) was not run. The new form is stronger than the first version of this stage and no weaker than the port check
on kiosk-versus-console; it has **not** been run against a stack, and I have not shown it failing. The page keeps its `/kiosk/` path across `page.reload()`. The
test's other assertions, and the second test in the file, are untouched.

**T024, the read-through** (a single context opening two apps): none. Every extra page in the touched specs is opened on its own `browser.newContext`
(`kiosk-comes-back`'s `rebooted`, `kiosk-touch-target`, `kiosk-identity-herd`, `spanning-wall`'s `kioskContext`, and the kiosk contexts in
`rule-switches-a-wall` and `wall-changes-its-scene`); no `context.newPage()` or `page.context().newPage()` mixes apps. A `search` for a second page opened inside an
existing context found none. `kiosk-comes-back` writes `sse.auth.wasAuthenticated` and the grant into `localStorage` of its **own** fresh context, so the shared
storage at `:5180` cannot reach it.

**T025.** The guard passes on the real tree (17 of 17) with the `:5174` entries gone and nothing stale. `playwright test --list` per project, compared by project,
file and title to the stage 1 baseline: **identical in all five** (`chromium` 107, `seed` 3, `kiosk` 32, `wall` 20, `cleanup` 3, with Playwright's own `Total:`
lines equal). `pnpm test:guards`: **369 passed, 0 failed**. `prettier`, `eslint --max-warnings 0`, `tsc -p e2e/tsconfig.json --noEmit` clean.

**T026, the small local stack run: NOT RUN** (same reason as T021). **Not observed locally**: the `kiosk` project, or any spec that opens the kiosk, at `/kiosk/` on
`:5180`. Stage 2's CI run is the first time the kiosk suite runs at the one origin; the sign-in at `/kiosk/` is already proven by `one-origin-sign-in.spec.ts`.

## Stage 2: review and dispositions (T027)

Two read-only reviewers (`frontend-reviewer`, `infra-reviewer`) were given the diff range and the intent, not this note's conclusions. They ran no stack and no
Playwright. **Neither found a blocker or a should-fix.** The frontend reviewer confirmed, for every kiosk-side spec: no relative `goto` remains (the only `goto('/')`
near the kiosk is the console's `admin.goto('/')`); the OIDC round trip under the base (`redirect_uri` `http://localhost:5180/kiosk/oidc/callback`, the realm's
`kiosk-web` client admits `:5180`, `onSigninCallback` returns under `/kiosk/`); every network matcher ignores host and port; the kiosk's REST calls already went to the
gateway and are now same-origin (no preflight to depend on); the hub stays direct and layout-composition's CORS list includes the gateway origin; no kiosk assertion
measures page delivery, so the gateway proxy cannot shift a latency figure; Vite 8's HMR client pings every 30 s, under YARP's 100 s idle timeout. The infra reviewer
confirmed the guard is exact (`MIGRATION` now holds only the five stage 3 entries with correct counts; crafted `:5174` re-introductions in a moved spec, the helper
and the config are each reported) and that the config diff is two lines.

| #     | Finding                                                                                                                                                                                                                                                             | Disposition                                                                                                                                                                                                                                                                                                                    |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| F1/I1 | The edited assertion (`.origin === ONE_ORIGIN`) is weaker than the old port check on kiosk-versus-console, passes vacuously if Keycloak is ever put behind the gateway (deferred in ADR-0192), and the note called it "stronger" and cited a red 3 that was not run | **Fixed.** The assertion now compares against the provider's real origin (captured from the route the test already intercepts), with a non-empty check, and asserts the page is still under `KIOSK_PATH`. The T023 text in this note and in `tasks.md` is corrected: it is not claimed to be shown failing.                    |
| F3    | A stale `:5173` comment in `spanning-wall.spec.ts`                                                                                                                                                                                                                  | **Fixed.**                                                                                                                                                                                                                                                                                                                     |
| F2    | The `oidc.user:` lookup (`.find(startsWith('oidc.user:'))`, in `kiosk-session.ts` and inline copies in seven specs) is ambiguous once the kiosk and the wall display share one origin's storage                                                                     | **Accepted for stage 2, carried to stage 3.** Every kiosk page lives in its own context, so nothing crosses today; making the lookup client-specific touches eight sites for no present effect. Stage 3 (T030) re-checks it when `kiosk-web` and `kiosk-wall` share the origin.                                                |
| I3    | `sse.auth.wasAuthenticated` (localStorage) and `sse.auth.redirectGuard` (sessionStorage) are shared by `/kiosk/` and `/wall/` in one browser profile                                                                                                                | **Noted, product-level, outside this diff.** The e2e suite is not exposed (separate contexts, no `storageState`). Named in stage 3's read-through.                                                                                                                                                                             |
| I2    | The guard knows ports, not paths: a new kiosk spec doing `goto('/')` on the kiosk project's base lands on the console and the guard reports nothing                                                                                                                 | **Considered, not done.** A guard for it needs to know which project a file belongs to (the unanchored `testMatch` regexes) and to exempt console contexts such as `kiosk-reconciliation`'s `admin.goto('/')`; the failure is usually loud (the console never shows "Pick a layout"). A possible follow-up, not part of #3247. |
| I4    | `develop`'s CI on stage 1's merge commit (`43694517856`) was still running (e2e shards 1/4 and 4/4)                                                                                                                                                                 | **Honoured**: this stage's PR is not opened until it is green.                                                                                                                                                                                                                                                                 |
| I5    | `kiosk-paints-a-reordered-overlay` is a known flake in this suite                                                                                                                                                                                                   | **Noted.** If it retries again it is the existing flake; the log is downloaded before any re-run.                                                                                                                                                                                                                              |

**What a green CI run of this stage will not show** (the infra reviewer's list, accepted): red 3 was never observed in a browser (it rests on the deterministic URL
resolution); the kiosk suite was not run locally, so this is its first run at `:5180`; a green run cannot tell a test that stayed on the kiosk from one that strayed to
the console and passed generically (now guarded in `kiosk-identity-refused` only); nothing about the wall display (stage 3), static-mode hosting, or deployment.

## Stage 3: the wall display (`:5175`) (branch `497-stage-3-wall`, cut from `develop` at `9aabaa58`, stage 2 merged in #3272)

`develop`'s own CI run on stage 2's merge commit (`43701562842`) was queued when this stage was started; the branch was cut and the static work done meanwhile, and **the PR is
not opened until that run is green.**

**T028, red 1 (the premise, no stack)**: `new URL('/', 'http://localhost:5180/wall/').href` prints `http://localhost:5180/`: a leading `/` throws away `/wall/`.

**T028, red 2 (the guard)**: with this stage's five `MIGRATION` entries deleted (commit `8b3b848a`), the real-tree test is red and names exactly the eight `:5175` lines:

```text
e2e/wall-outlives-its-session.spec.ts:105   e2e/wall-survives-a-lockout.spec.ts:80   e2e/wall-survives-a-process-death.spec.ts:29
e2e/wall-withdrawal.spec.ts:79, :80, :107, :108                                        playwright.config.ts:84
```

**Red 3 (a wall spec at the one origin with the helper unchanged, on a booted stack): NOT RUN**, for the same reason as stages 1 and 2 (no stack was booted; a full stack has run this
machine out of memory three times). The gap is named: it rests on red 1.

**T029, the move.** `playwright.config.ts`: the `wall` project's base URL is `ONE_ORIGIN` (and its comment says `/wall/`, and that a test that opened `/kiosk/` would silently exercise the
ordinary kiosk). `support/wall-session.ts`: `signInAsWallDisplay`'s default `url` is `WALL_PATH` (it was `'/'`). `wall-outlives-its-session` (the rebooted context's base and its `goto`),
`wall-withdrawal` (four contexts) take `ONE_ORIGIN`; the two persistent-context specs' `WALL` constants are `${ONE_ORIGIN}${WALL_PATH}`. Only origins and paths changed; every caller of
`signInAsWallDisplay` either runs in the `wall` project (default path) or passes an explicit URL. After this stage **no `5174` or `5175` remains anywhere in `e2e/` or the config**, code or comment.

**T030, the read-through.** No context opens two apps. The two persistent-context specs launch each context with its own `mkdtemp` profile holding only the wall's grant
(`wall-survives-a-process-death` deliberately reuses _its own_ profile for the relaunch); `wall-withdrawal`'s two screens are separate `browser.newContext`s, so "two screens must not share a
session" still holds at one origin; `wall-outlives-its-session`'s rebooted context is its own. **The `oidc.user:` lookups** (`.find(startsWith('oidc.user:'))`, and `wall-withdrawal`'s
`.includes`) are unambiguous: no context holds both a `kiosk-web` and a `kiosk-wall` grant, so nothing was changed (the stage 2 review's condition for changing them was "if any could coincide"; none
can). **The keys the two modes share at the product level** are `sse.auth.wasAuthenticated` (localStorage) and `sse.auth.redirectGuard` (sessionStorage): the e2e suite is not exposed because every
second app has its own context. Cookies are per host, not per port, so the Keycloak cookies the lockout and process-death specs clear were already shared across ports before this change.

**T031, the ratchet is removed, and the proofs.** `MIGRATION` is empty, so it was **deleted with the code that read it and the five test cases that exercised it** (the guard has 12 cases, was 17; the
stage 1 note records the evidence that the ratchet worked in both directions). `PERMANENT` keeps its `match` and its stale-entry check. **SC-001:** a search of `e2e/`, the config, `scripts/` and
`.github/` (code **and** comments) for a fixed UI port finds only the guard's own files, the readiness script and its test, and three **comments** in `operator-shell-federation.spec.ts` about the
_remotes'_ dev ports (`:5176`/`:5177`); no code names a fixed UI port outside the permanent set. `playwright test --list` per project, compared by project, file and title to the **original** baseline:
**identical in all five** (`chromium` 107, `seed` 3, `kiosk` 32, `wall` 20, `cleanup` 3; Playwright's own `Total:` lines equal). `pnpm test:guards`: **364 passed, 0 failed**. `prettier`, `eslint
--max-warnings 0`, `tsc -p e2e/tsconfig.json --noEmit` clean.

**T032, the small local stack run: NOT RUN** (same reason). **Not observed locally**: the `wall` project, or any spec that opens the wall display, at `/wall/` on `:5180`. This stage's CI run is
the first time the wall suite runs there; sign-in at `/wall/` is already proven by `one-origin-sign-in.spec.ts`.

## Final summary against the spec (the three stages)

**Latency leg touched: none**, in any stage. No product source, AppHost, realm, gateway or `apps/` file changed across #3271, #3272 and this PR; the diffs are confined to `e2e/`, `scripts/`,
`playwright.config.ts` and `specs/`.

| Requirement                                                            | Status                                                                                                                                                                                                                                           |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| FR-001 the root-assuming helper observed failing at the one origin     | **Not observed in a browser**, in any stage. What was observed is the deterministic premise (a leading `/` replaces `/kiosk/` and `/wall/`), quoted three times; the failure is predicted from it.                                               |
| FR-002 the console projects at the one origin                          | **Observed in CI** (stage 1, #3271: 161 passed, 1 flaky, every moved spec green)                                                                                                                                                                 |
| FR-003 the kiosk at `/kiosk/` and the wall display at `/wall/`         | Kiosk **observed in CI** (stage 2, #3272). **Wall pending this PR's CI run.**                                                                                                                                                                    |
| FR-004 / FR-005 the guard, with a written reviewed exception list      | **Done, red first, in CI** (the `frontend` job runs it); the exception list is `PERMANENT` with a reason per entry                                                                                                                               |
| FR-006 the readiness step gates the three paths and names a failure    | **Done, red first**; the **positive path observed on a real stack in CI** (`/` 1 s, `/kiosk/` and `/wall/` 0 s); the **failure path only against a stub `curl`**; it is a **reachability gate, not a warm-up** (corrected in review)             |
| FR-007 nothing weakened, deleted or skipped; no product source touched | **Observed**: the diffs are added imports, replaced origins and paths, comments. **One assertion was edited** (`kiosk-identity-refused`, stage 2, declared, **not shown failing**). The guard's own ratchet cases were removed with the ratchet. |
| FR-008 no spec dropped or re-routed                                    | **Observed at every stage**: `--list` per project identical by project, file and title                                                                                                                                                           |
| FR-009 the owner's decision on the fixed ports recorded on the epic    | **Pending the merge of this PR** (T034)                                                                                                                                                                                                          |
| FR-010 retries seen, not absorbed                                      | Stage 1: 161 passed + 1 flaky vs a baseline of 161 + 1. Stage 2: 161 + 1 vs 160 + 2. **Stage 3 pending.** The one recurring flake is `kiosk-paints-a-reordered-overlay`, present in every baseline.                                              |
| FR-011 the fixed ports keep working                                    | **By construction, not by CI**: no product, AppHost or realm file changed. After this PR no e2e spec opens any of `:5173`, `:5174` or `:5175`, so CI no longer exercises them.                                                                   |
| FR-012 delivered in stages, one PR each, with a ratchet                | **Done** (#3271, #3272, this PR); the ratchet reached zero and was removed                                                                                                                                                                       |
| SC-001 zero references outside the exceptions                          | **Observed** (the search above and the guard)                                                                                                                                                                                                    |
| SC-002 every project passes at the one origin on a fresh stack in CI   | Console and kiosk **observed**; **wall pending this PR's run**; 0 specs dropped, observed                                                                                                                                                        |
| SC-003 the guard fails on a hard-coded origin, naming it               | **Observed** on fixtures, on the real tree (31 lines at stage 1, 9 at stage 2, 8 at stage 3) and with in-memory probes against the real files                                                                                                    |

**Not observed, anywhere:** red 3 in any stage, the readiness gate's failure path on a real stack, the edited assertion failing, a local run of the kiosk or wall suite at the one origin (three
stack boots exhausted this machine's memory; the owner chose CI as the first proof), and the fixed-port developer paths (untested by CI from here on).
