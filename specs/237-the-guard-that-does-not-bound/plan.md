# Plan 237 — The guard that does not bound

**Spec**: [spec.md](spec.md) · **Issue**: #2385 · **Phase**: 2 (Plan)
**ADRs**: 0036, 0037, 0109, 0139, 0144 · **Constitution**: §IV N/A; §Testing red-first.

## Context and layers

Not a bounded context. Test infrastructure in the Playwright `cleanup` project (`playwright.config.ts:104-108`,
`testMatch: /.*.teardown.ts/`, `workers: 1` in CI) and the `node --test` guard suite (`pnpm test:guards`,
glob `scripts/**/*.test.mjs`). No domain, no messaging, no contracts, no Aspire resource. The
"no cross-context reference" rule is not engaged.

## Design

Reuse, don't re-implement. `recoverAndRetry(now, deadline, timeoutMs, signIn, retry)` already does
both halves (deadline re-check; `Promise.race` against a cap). The layouts site differs only in that
its sign-in is guarded by `looksSignedOut`. The guard goes **inside** the bounded thunk, so:

- the deadline check happens before anything, including the guard;
- the guard's own `count()` is inside the cap (harmless, and it keeps one bounded unit).

```ts
// e2e/support/archive-e2e-layouts.recovery.ts — final shape
export function recoverLayout<T>(now, deadline, timeoutMs, looksSignedOut: () => Promise<boolean>,
                                 signIn: () => Promise<void>, retry: () => Promise<T>) {
  return recoverAndRetry(now, deadline, timeoutMs,
    async () => { if (await looksSignedOut()) await signIn(); }, retry);
}
```

Why a layouts-specific wrapper rather than passing the thunk inline: it is the **testable seam for the
call site's actual composition** (guard + bound + order). Testing `recoverAndRetry` alone would be
green today and prove nothing about this file — the "guard disconnected from its call site" failure
PR #2384 explicitly rejected. The wrapper is four lines and has one consumer; no generality is added.

### Call site (`archive-e2e-layouts.teardown.ts:77-82`)

Replace the two lines with the camera teardown's shape (`retire-e2e-cameras.teardown.ts:116-129`):

```ts
const recovery = await recoverLayout(Date.now(), deadline, RE_SIGN_IN_TIMEOUT_MS,
  () => looksSignedOut(page), () => signInAsOperator(page), () => archiveByName(page, name));
if (!recovery.attempted) { outOfTime = true; skipped.push(name); break; }
if (recovery.result === 'archived') archived++; else skipped.push(name);
```

Add `const RE_SIGN_IN_TIMEOUT_MS = 30_000;` beside `DEADLINE_MS` with a one-paragraph *why* (mirror
the camera file's `:51-59`, pointing at #2382's 15 s fresh-page figure). Update the `:77-79` comment to
say both halves are bounded and why an in-place retry is not attempted (bound and skip). Do not touch
`archiveByName`, `looksSignedOut`, `disposableNames`, `DEADLINE_MS`, or `cleanup.setTimeout(600_000)`.

Note the outcome type: `archiveByName` returns `'archived' | 'nothing-to-do' | 'refused'`. On retry,
anything but `'archived'` is skipped — unchanged from today's `:81-82`.

### Relocation (FR-005)

`git mv e2e/support/retire-e2e-cameras.recovery.ts e2e/support/sweep-recovery.ts` and
`git mv scripts/retire-e2e-cameras-recovery.test.mjs scripts/sweep-recovery.test.mjs`. Edits:
- `sweep-recovery.ts` header comment: say it bounds a teardown sweep's mid-sweep re-sign-in, name
  both consumers, fix the test-file path. Code body byte-identical.
- `sweep-recovery.test.mjs`: import path only (and the path in its header comment). Assertions
  **unmodified** — this is the characterisation for the move.
- `retire-e2e-cameras.teardown.ts:3`: import path only.

`sweep-recovery.ts` does not match `/.*.teardown.ts/` (no "teardown" substring), nor does
`archive-e2e-layouts.recovery.ts`.

**Why move rather than import from the camera-named file**: after this change it has two consumers,
and a layouts teardown importing from `retire-e2e-cameras.*` reads as a cross-file accident. The move
is three mechanical edits, the issue asks for it, and nothing else references the path except the
historical `specs/186/tasks.md` (left alone). It lands as its **own commit**, green before and after,
so the behaviour-changing commits stay reviewable on their own (ADR-0036: a move changes shape, a fix
changes the bug — separate commits, not separate issues, because the move carries no behaviour).

## Commit sequence (each builds and passes its own gate on its own — ADR-0087 rebase-merge)

1. `refactor(e2e): move recoverAndRetry to a neutral sweep-recovery module` — relocation; `test:guards`
   green with the moved test's assertions unmodified.
2. `test(e2e): guard the layout teardown's re-sign-in against running unbounded` — new
   `archive-e2e-layouts.recovery.ts` in **today's unbounded shape**, unwired, plus
   `scripts/archive-e2e-layouts-recovery.test.mjs` (R1–R3, G1). Observed red: R1, R2 assertion;
   R3 2000 ms timeout; G1 green. **Note:** this commit leaves `test:guards` red on purpose, exactly as
   #2382's `8e2f879e` did; it builds (tsc/lint) on its own.
3. `fix(e2e): bound the layout teardown's re-sign-in to its own budget` — `recoverLayout` delegates to
   `recoverAndRetry`; wired into the teardown; `RE_SIGN_IN_TIMEOUT_MS`; comment update. All green.

## Verification

`pnpm test:guards`, `pnpm typecheck:e2e`, `pnpm lint:e2e`, `playwright test --list --project=cleanup`
(three teardowns exactly). Counterfactual per spec §6. No Aspire stack needed; phase 5 is the guard
run plus the `--list` check, and the CI duration effect is recorded as a prediction.

## Latency

N/A — no event→overlay leg (constitution §IV).

## Risks

- `node --test` imports `.ts` via Node's type stripping, as the existing guard already does; the new
  module must use only erasable TS (no enums/parameter properties) — same constraint as today.
- Stacking: none. Cut from `develop`; no dependency on an unmerged branch.
