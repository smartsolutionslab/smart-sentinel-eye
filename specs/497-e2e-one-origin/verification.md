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
