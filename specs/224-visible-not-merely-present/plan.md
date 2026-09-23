# Plan 224 — Visible, not merely present

**Spec**: `spec.md` · **Issue**: #2304 · **Phase**: 2 (Plan)
**Date**: 2026-09-23 · **Branch**: `2304-visible-not-merely-present`
**Baseline**: `70b52c96` (`origin/develop`, fetched 2026-09-23)

---

## 1. Context and layers

**There is no bounded context here.** This slice is entirely inside
`apps/shared`, the React/TypeScript package ADR-0074 introduced so the kiosk wall
and the operator console share composites rather than fork them. No C#, no
domain model, no Application layer, no messaging, no persistence.

The layer vocabulary that *does* apply is the frontend package's own:

| Layer | Touched | What |
|---|---|---|
| `apps/shared/package.json` | **yes** | one devDependency: `@testing-library/jest-dom` 7.0.1 |
| `apps/shared/vitest.config.ts` | **yes** | one key: `setupFiles` |
| `apps/shared/src/test/` | **new** | one file, one line |
| `apps/shared/src/ui/**/*.test.tsx` | **yes** | 32 matcher call sites in 7 files |
| `apps/shared/src/ui/**/*.tsx` (production) | **no** | **nothing.** This is what makes the change behaviour-preserving. |
| `apps/kiosk-web`, `apps/management-web` | **no** | already correct |
| `src/**` (all .NET) | **no** | — |

**Constitution §II does not bind**: no domain model, no primitive-typed state, no
C# type. `PrimitiveBoundaryTests` and `HandlerDeconstructionTests` are not
reachable from this diff.

---

## 2. Design

### 2a. The wiring — one global `setupFiles`, not a `projects` split

`apps/shared/vitest.config.ts` gains exactly one key:

```ts
  test: {
    globals: true,
    environment: 'node',
    setupFiles: ['./src/test/setup.ts'],          // ← added
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
```

**The decision this encodes**, and the alternative it rejects: `apps/shared` runs
**19 jsdom files and 14 node files** (spec §1.3), and the split is declared *per
file* by a line-1 `// @vitest-environment jsdom` pragma, not by directory. So
there is no `include` glob that separates them and a `projects` split would have
to duplicate the pragma information in the config — two places to keep in step,
and the pragma would win anyway.

A single global `setupFiles` is correct **only if** importing
`@testing-library/jest-dom/vitest` is harmless under `environment: 'node'`. That
was not assumed. Spec §2 records the run: a `// @vitest-environment node` test in
a package with the jest-dom setup active passed while asserting
`typeof globalThis.document === 'undefined'`. The import calls `expect.extend`
and touches no DOM global at load time.

**Mirrors both apps exactly** — `apps/kiosk-web/vite.config.ts:47` and
`apps/management-web`'s equivalent both read
`setupFiles: ['./src/test/setup.ts']`. ADR-0036: reuse the existing pattern, do
not invent a shared-package variant of it.

### 2b. The setup file — one line, and why not two

`apps/shared/src/test/setup.ts`:

```ts
import '@testing-library/jest-dom/vitest';
```

Byte-identical to `apps/kiosk-web/src/test/setup.ts`.
`apps/management-web/src/test/setup.ts` additionally carries
`configure({ asyncUtilTimeout: 10_000 })` with a 20-line ADR-0150 rationale about
contended CI runners. **That is not copied.** It is a remedy for a flake
`apps/shared` has not had, and copying a fix for an unobserved problem is the
speculative generality ADR-0036 forbids. If `apps/shared` later flakes the same
way, that is ADR-0150's issue and it arrives with its own evidence.

**Placement is load-bearing, not cosmetic.** `apps/shared/tsconfig.json` sets
`"rootDir": "src"` and `"include": ["src/**/*"]`. jest-dom's TypeScript support
is a **module augmentation** of `vitest`'s `Assertion` interface, delivered by
the `/vitest` entry point — so the augmenting import must be inside the
TypeScript program, or `.toBeVisible()` is a compile error even though the
runtime matcher works. A setup file at the package root would give a green
`vitest run` and a red `tsc --noEmit`. `src/test/setup.ts` is inside both
`rootDir` and `include`. This is spec §AS-6, and it is the only trap in the slice.

`vitest.config.ts`'s `include` globs match only `*.test.ts(x)`, so `setup.ts` is
not itself collected as a suite.

### 2c. The dependency

`apps/shared/package.json` `devDependencies` gains, in the existing alphabetical
position next to `@testing-library/react`:

```json
    "@testing-library/jest-dom": "7.0.1",
```

**Exact version, no range** — every entry in every `apps/*/package.json` is
exact, and 7.0.1 is what both apps pin. A `^7.0.1` here would let the workspace
resolve three different jest-dom trees; a *different* pinned version would be
drift with no decision behind it.

`pnpm-lock.yaml` updates as a consequence. pnpm is workspace-pinned at 10.30.3
and does not hoist, so this is a genuine new install into
`apps/shared/node_modules`, not a link to an existing one.

### 2d. The conversion — a mechanical, bounded edit

32 call sites across 7 files (spec §1.2's table is the authority on which):

```
.toBeDefined()   →  .toBeVisible()      (23 sites)
.toBeTruthy()    →  .toBeVisible()      ( 9 sites)
```

**Nothing else on the line changes.** Not the query, not its argument, not the
`await`/`act` around it, and not the custom failure message at
`CameraViewerMedia.test.tsx:385` — which is a three-line
`expect(subject, "message").toBeDefined()` and stays a three-line
`expect(subject, "message").toBeVisible()`.

**The invariant that makes this safe to verify mechanically**: after the change,
`apps/shared/src` contains exactly **6** remaining `.toBeDefined()`/
`.toBeTruthy()` occurrences, and they are precisely spec §1.2's out-of-scope
table — four string/number subjects in two jsdom files, two non-DOM subjects in
two node files. Any other number means the edit over- or under-reached.

---

## 3. Entities, value objects, invariants

**None.** No domain model is created, read or changed; there is no aggregate, no
value object, no `Result<T, Error>`, no `Option<T>`, no `Ensure.That`.

The invariants this slice does carry are about the **diff**, and they are what
phase 6 should check:

| # | Invariant | How it is checked |
|---|---|---|
| **I1** | No production source file changes | `git diff --name-only` contains no `apps/shared/src/**/*.tsx` that is not a `*.test.tsx` |
| **I2** | Every changed test line differs **only** in the matcher | `git diff -U0` — each hunk is a one-token swap |
| **I3** | 32 converted, 6 left | the grep in spec §4 step 7 returns exactly 6 |
| **I4** | No `.toBeVisible()` on a non-element | implied by I3 + a green suite; a non-element throws at runtime |
| **I5** | Pass/fail counts per file identical before and after | spec §4 steps 2–3 |
| **I6** | `.toBeVisible()` type-resolves | `pnpm --filter @smart-sentinel-eye/shared typecheck` |

---

## 4. Messaging (domain → integration event)

**N/A.** No RabbitMQ, no Wolverine, no `Shared.Contracts` message, no SignalR
hub, no domain event, no integration event. Nothing crosses a process boundary.

---

## 5. Boundary rules

- **No cross-context project references**: unreachable — no .NET project is
  touched, so `NetArchTest` sees an unchanged assembly graph.
- **Frontend boundary**: `apps/shared` is the *shared* package and must not gain
  a dependency on either app. It does not — `@testing-library/jest-dom` is a
  third-party devDependency, and the only cross-package relationship created is
  *version parity* with the two apps, which is the asymmetry the issue is about.
- **Workspace boundary**: the workspace root's `package.json` is **not** touched.
  jest-dom belongs to the three packages that render DOM, not to the root.
- **`apps/shared` exports**: the `exports` map in `package.json` is **not**
  touched. `src/test/setup.ts` is internal and deliberately unexported — nothing
  outside the package may import it.

---

## 6. Risk register

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| **R1** | A converted assertion goes **red** because the element really is inline-hidden | low — all 32 targets read for inline `style`/`hidden`; the only two with inline styles carry colour and font-size only (spec §6.2) | **Block, do not adjust** (spec §6.1 rule 4). A red here is a genuine finding: the test was asserting a node an operator could not see. Report it; it becomes its own issue. |
| **R2** | The Radix `alertdialog` sites (`ConfirmDialog.test.tsx:46,114`) fail on a portal `hidden` attribute | low — `getByRole('alertdialog')` matches only an open dialog | R1's rule. |
| **R3** | `typecheck` red: `.toBeVisible()` not on `Assertion` | low, but this is the slice's one real trap | §2b's placement decision; T4 verifies it independently of the suite. |
| **R4** | The node-environment files break on the new setup file | low — empirically disproven (spec §2) | T5 runs the whole suite, both environments. |
| **R5** | `pnpm install` resolves a jest-dom version other than 7.0.1 | very low | exact pin, and the lockfile diff is reviewed. |
| **R6** | The edit over-reaches into the 6 out-of-scope sites | **medium** — a naive repo-wide sed would hit all 38 | I3's grep is the gate, and it is a task (T7), not a reviewer's memory. |

R6 is the one worth naming loudly: the obvious way to do this change is the wrong
one. `sed -i 's/toBeDefined()/toBeVisible()/g'` produces a red suite
(`received value must be an HTMLElement`) on four of the six, and a *silently
wrong* assertion on the other two. The conversion is **site-by-site against spec
§1.2's table**.

---

## 7. Definition of done

1. `pnpm --filter @smart-sentinel-eye/shared test` — green, per-file counts
   identical to the `70b52c96` capture.
2. `pnpm --filter @smart-sentinel-eye/shared typecheck` — clean.
3. `pnpm --filter @smart-sentinel-eye/shared lint` — clean at `--max-warnings 0`.
4. I1–I6 all hold.
5. The AS-2 counterfactual run, its failure quoted in the PR body, and reverted.
6. `pnpm -r --filter "./apps/**" test` — green (the other two packages unaffected).

**"Done when it compiles" is not available here**: a green suite is the *pre*
condition of this change, not its proof. The proof is §7.5 — the deliberate
`display: none` that makes a converted assertion fail. Without it the PR
demonstrates only that 32 lines were retyped.
