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
