# Implementation Plan: Bump `@hookform/resolvers` 6.0.0 → 7.0.5

**Branch**: `488-hookform-resolvers-7` | **Date**: 2026-11-10 | **Spec**: [spec.md](./spec.md) | **Issue**: #3239

## Summary

Move `@hookform/resolvers` to `7.0.5` in `management-web` and `management-cameras`. The expected diff is two manifests and the lockfile.
Prove behaviour is preserved by capturing both apps' suites green on the **lockfile-true** unmodified tree first, then identical after;
and prove the suites actually depend on the resolver by a per-form counterfactual.

## Technical Context

pnpm workspace (`pnpm@10.30.3`), React 19, React Hook Form 7.91.0, Zod 4.6.7, vitest 5.1.0 under jsdom. Seven `zodResolver(<schema>)`
call sites in seven files, all importing `@hookform/resolvers/zod`. No other app depends on the package.

## Constitution Check

§IV: not on the path. ADR-0079: React Hook Form + Zod is the decided stack; this keeps it. ADR-0036: smallest change, no drive-by.
§Testing: behaviour-preserving, so characterisation; nothing to observe red. The lane may not weaken a gate to reach green, so a test
that fails after the bump is reported, not edited. No ADR needed.

## Decisions

**D1. The baseline is taken on a lockfile-true install.** This machine's `node_modules` is stale: it holds `@hookform/resolvers` 5.9.1
against a manifest and lockfile at 6.0.0 (found by comparing manifests to the installed tree, 2026-11-10). A baseline on it would
characterise the wrong tree. So: `pnpm install --frozen-lockfile` on the unmodified branch, confirm the installed version is `6.0.0`
(read it from `node_modules`, not from the manifest), then run the suites.

**D2. The suites.** Per app, `vitest run` in `apps/management-web` and `apps/management-cameras`, **one vitest at a time** (the root
`test` script already uses `--workspace-concurrency=1`). Counts (files, tests) recorded from the runner's summary. `apps/shared` is not
run: it does not depend on the package, and FR-001 keeps it out of the diff.

**D3. The bump is a manifest edit, then a lockfile-only resolve.** Edit the two `package.json` pins to `7.0.5`, run
`pnpm install --lockfile-only`, then `pnpm install --frozen-lockfile` to materialise it and read the installed version back.
`git diff --stat` must show exactly `apps/management-web/package.json`, `apps/management-cameras/package.json`, `pnpm-lock.yaml`.
If `pnpm` moves any other package in the lockfile, that is FR-006 failing: stop and report rather than hand-edit the lockfile.

**D4. FR-006 is checked from the diff, not by eye.** Extract the removed and added `package`/`snapshot` keys from the lockfile diff:
every changed key must contain `@hookform/resolvers`, and no second `zod@` or `react-hook-form@` snapshot may appear.

**D5. FR-007 by counterfactual, per form.** For each of the seven call sites, replace `zodResolver(<schema>)` with a resolver that
accepts everything (`async (values) => ({ values, errors: {} })`), exactly once, in a named substitution script; run that form's test
file; **expect at least one failure**. A form whose tests stay green with the resolver bypassed has no test that reaches it: record it
in `verification.md` and file it, do not add a test here. The probe runs **after the bump commit** and restores only the file it broke,
from git; the runner refuses to start on a dirty tree. `useOverlayDraftFormState.ts` is probed through `OverlayDraftForm.test.tsx`.
Probing on the bumped tree is the point: it shows the 7.0.5 resolver is what the tests exercise.

**D6. Gates.** In both apps: `eslint src --max-warnings 0` (check its **exit code**), `tsc --noEmit`, and `prettier --check` on the
two `package.json` files and the lockfile-adjacent files it covers. `eslint` clean does not mean `prettier` clean, and CI's format step
runs first.

**D7. Production code, tests and snapshots are read-only.** `git diff origin/develop --stat` shows the three files only (SC-002).

## Phases and commits

1. `docs(488): plan`, `docs(488): tasks`.
2. `build(deps): bump @hookform/resolvers to 7.0.5` (two manifests + lockfile; no other file).
3. `docs(488): verification note, tick tasks` (baseline and after-counts, D4 diff extract, D5 table, gates).

The bump commit is made **before** the D5 counterfactual so the probe has a clean tree to restore from.

## Risks and how they are covered

- **The baseline is on the wrong tree.** D1 reads the installed version from disk before running.
- **A hidden behaviour change in the zod resolver that the suites do not see.** D5 shows each form's tests do reach the resolver; a
  form that does not is recorded and filed, which is the honest limit of a characterisation.
- **The lockfile drags other packages along.** D3/D4 stop on it.
- **Merge friction with #3240.** Both rewrite `pnpm-lock.yaml`. The second to merge rebases; `pnpm install --lockfile-only` re-resolves
  rather than hand-merging.
- **A flaky unrelated test fails after the bump.** One retry of that file alone, saving the failure first; a failure that reproduces
  is the bump's until shown otherwise.

## Project Structure

```
specs/488-hookform-resolvers-7/
  spec.md  plan.md  tasks.md  verification.md  checklists/requirements.md
apps/management-web/package.json        (one line)
apps/management-cameras/package.json    (one line)
pnpm-lock.yaml                          (resolvers rows only)
```
