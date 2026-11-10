# Implementation Plan: Bump `jsdom` 30.1.3 → 31.0.0

**Branch**: `489-jsdom-31` | **Date**: 2026-11-10 | **Spec**: [spec.md](./spec.md) | **Issue**: #3240

## Summary

Move `jsdom` to `31.0.0` in the five frontend apps. The expected diff is five manifests and the lockfile. Because 31.0.0 replaces the CSS
engine that jest-dom and user-event read through, the proof is stricter than for a pure version bump (#3239): a **twice-run baseline** on a
lockfile-true tree, identical file/test/skipped counts after, a diff of the virtual-console noise, and a sample of visibility/style
assertions shown able to fail.

## Technical Context

pnpm workspace (`pnpm@10.30.3`), vitest 5.1.0, React 19. Four apps set `environment: 'jsdom'` (`management-web` 102 test files,
`management-cameras` 24, `kiosk-web` 39, `management-event-ingestion` 4); `apps/shared` defaults to `node` with 62 of 92 test files opting
in per file. Production code makes no `getComputedStyle` call; the exposure is jest-dom's `toBeVisible` / `toHaveStyle` (13 files) and
user-event's `pointer-events` check (78 files).

## Constitution Check

§IV: not on the path (a dev dependency that ships in no bundle; no source change). §Testing: behaviour-preserving, so characterisation,
nothing to observe red. The lane may not weaken a gate to reach green: a test that fails after the bump is **reported, not edited**. No ADR.

## Decisions

**D1. Baseline on a lockfile-true install, twice.** `pnpm install --frozen-lockfile` on the unmodified branch (cut from `origin/develop`,
which has `jsdom` 30.1.3 and `@hookform/resolvers` per develop), then read the installed `jsdom` version from `node_modules` in **each
of the five apps**, and list the `jsdom@*` directories under `node_modules/.pnpm` (must be one). Local Node version recorded against the
engines range. Run each app's full suite **twice**; the first run is the baseline, the second tells us what normal variance looks like
(#2641: the singleton-store-plus-fake-timers shape is timing-sensitive).

**D2. One runner, counts from JSON, noise from the log.** `run489.cjs` (scratchpad) runs `vitest run --reporter=json --outputFile=…` for
one app at a time, keeps the combined stdout/stderr, and prints: files, tests passed, failed, **skipped/todo**, and the number of
`stderr |` / `stdout |` blocks. Foreground, **one vitest at a time**, apps in a fixed order (`shared`, `kiosk-web`,
`management-event-ingestion`, `management-cameras`, `management-web`). The JSON, not the console summary, is the source of the counts.

**D3. The bump is a manifest edit, then a lockfile-only resolve.** Set `jsdom` to `31.0.0` in the five `package.json` files (exact pin,
same value; FR-002), `pnpm install --lockfile-only`, then `pnpm install --frozen-lockfile`, then read the installed version back from
disk in each app. `git diff --stat` must be exactly the five manifests and `pnpm-lock.yaml`.

**D4. FR-008 from the diff.** Extract the added and removed package keys from the lockfile diff. Every changed key must be `jsdom` or a
package that `pnpm why <pkg>` shows is reached through `jsdom`; the new CSS engine may bring its own transitive dependencies, and that is
expected, not a failure. A key reached by something else, a second `jsdom`, or an unrelated package moving is FR-008 failing: **stop and
report**, do not hand-edit the lockfile. `@napi-rs/canvas` is an optional peer: it must not become an installed package.

**D5. After the bump, same runner, same order, once; twice if anything differs.** Counts equal the baseline's (files, passed, skipped)
and no test file is in the diff. Then **diff the noise**: normalise the `stderr |` / `stdout |` blocks (strip paths, timings, ids) and
compare the two sets; every message present after and absent before is read and recorded (FR-005). A failure is first re-run **alone,
once**, with the first failure saved verbatim; one that reproduces on 31 and not on 30 is the bump's, classified under User Story 1.

**D6. FR-006 — are the visibility and style matchers live on 31?** Sample **three** of the 13 `toBeVisible` / `toHaveStyle` files, one
from each of `management-web`, `kiosk-web` and `shared`, mixing both matchers. For each, one named exactly-once substitution **in the
component under test** that must change what the matcher sees (hide the element, or change the asserted style value), then run that
file and expect at least one failure. This runs **after the bump commit**, restores only the file it broke, from git, and the runner
refuses to start on a dirty tree. A sample that stays green is recorded as a finding and filed, not fixed. The exact files and
substitutions are chosen when the files are read, and written into `verification.md` before they are run.

**D7. Gates.** `eslint src --max-warnings 0` (exit code read) and `tsc --noEmit` in all five apps; `prettier --check` on the five
manifests. `eslint` clean does not mean `prettier` clean.

**D8. Production code, tests and snapshots are read-only.** `git diff origin/develop --stat -- . ':!specs' ':!.specify'` shows the
manifests and the lockfile only (SC-002).

**D9. If the bump is red.** A reproducing failure is **not** adjusted and **no PR is opened red**. The verbatim failures go in
`verification.md` and as a comment on #3240, with the jsdom 31 release-note line that explains each, and the issue returns to you for a
decision (hold at 30, or a separate behaviour-changing issue to adapt the test). That is the honest outcome of a characterisation that
finds a behaviour change.

## Phases and commits

1. `docs(489): plan`, `docs(489): tasks`.
2. `build(deps): bump jsdom to 31.0.0` (five manifests + lockfile only), made **before** the D6 probe so the probe has a clean tree to
   restore from.
3. `docs(489): verification note, tick tasks`.

## Risks and how they are covered

- **The baseline is on the wrong tree.** D1 reads the installed version from disk in all five apps and checks for a single `jsdom`.
- **Variance is mistaken for a regression.** D1's second baseline run and D5's single-file re-run.
- **A vacuous matcher hides a change.** D6 shows a sample can fail; the limit (a matcher that was already vacuous) is stated in the spec.
- **The new engine produces new console noise that nobody reads.** D5 diffs the normalised messages.
- **Merge friction with #3239 / PR #3242.** Both rewrite `pnpm-lock.yaml`; the second to merge rebases and re-resolves with
  `pnpm install --lockfile-only`.
- **Disk or time.** Five apps × (two baseline + one after) is about thirty minutes of foreground vitest; the runner writes after every
  app so a harness kill loses at most one.

## Project Structure

```
specs/489-jsdom-31/
  spec.md  plan.md  tasks.md  verification.md  checklists/requirements.md
apps/{management-web,management-cameras,kiosk-web,management-event-ingestion,shared}/package.json   (one line each)
pnpm-lock.yaml                                                                                      (jsdom and its tree)
```
