# Implementation Plan: The latch three files still shared

**Spec**: [spec.md](./spec.md) · **Issue**: #2670 · **Template**: `1d536690` (spec 290, #2641)

**Phase-4a colour**: **behaviour-preserving** (characterisation, test-harness only). Evidence is the
counterfactual pair plus unmodified-green runs, not a new red test — there is no new behaviour to
drive red. The probe's red half is not a phase-4a red test; it is the proof that the channel being
removed exists.

**Engineer**: `frontend-engineer` (Vitest/React test harness in two front-end packages).
**Reviewer**: `frontend-reviewer`. No security surface.

## 1. Constitution / ADR check

| Rule | Status |
|---|---|
| ADR-0075 one store per app | Unchanged. Production `app/store.ts` untouched; tests build their own instances via the app's own construction (`createApiStore`), as `OverlayDraftFormResolvePreview.test.tsx` and every `management-cameras` test already do. |
| ADR-0139 / §Testing characterisation | Tests captured green before, pass unmodified after; no `expect(` edits (FR-003). |
| ADR-0150 waiting is a condition | The probe's 250 ms real wait never lands in a committed file. No permanent guard is added, for spec 290's reason: a guard needs a real sleep, and the fix removes the channel structurally. |
| ADR-0036 smallest change | Three files, store construction only. Kiosk pair and the rAF frame-loop hazard excluded (spec *Edge Cases*). |
| ADR-0144 lane may not write an ADR | None needed — spec 290's merged pattern. |
| Bounded contexts / Shared.Contracts / §IV | N/A — no backend, no contract, no latency leg. |

## 2. Design — per file

Common shape (mirrors `1d536690`): a local `createStore()` wrapping the file's **existing**
store construction, a `let store: ReturnType<typeof createStore>;`, and `store = createStore();`
in a `beforeEach` that runs **before** any `vi.useFakeTimers()` for the same test (FR-002 — RTK
captures `window.requestAnimationFrame` at store creation; today's stores are all built on real
timers). Every `<Provider store={store}>` site keeps its text; it now reads the per-test binding.

### 2.1 `apps/management-web/src/features/overlays/OverlayDraftForm.test.tsx`

- Line 6: `import { store } from '../../app/store.js';` →
  `import { apiSlices } from '../../app/store.js';` plus
  `import { createApiStore } from '@smart-sentinel-eye/shared/store';`
  (management-web already depends on that entry point — `app/store.ts` imports it).
- Module scope: `function createStore() { return createApiStore(apiSlices); }` and
  `let store: ReturnType<typeof createStore>;`. Using `apiSlices` reproduces the singleton's exact
  slice set and middleware, so no un-mocked hook can find its reducer missing — unlike
  spec 290's hand-picked two slices, which would need an audit of every hook `OverlayDraftForm`
  reaches.
- The **file-level** `beforeEach` (line 96-104) gains `store = createStore();` as its first
  statement. Vitest runs file-level hooks before `describe`-level ones, so in *Frame capture* the
  store exists before that describe's `vi.useFakeTimers()` (line 730).
- `renderDialog` (line 108) and `renderControlledDialog` (line 717) are unchanged in text.
- Importing `apiSlices` still evaluates `app/store.ts`, which builds the (now unused) singleton
  at module load. Harmless — no timers, no subscription — and keeps the slice list single-sourced
  (`store.test.ts`, spec 303 FR-005, guards that array).

### 2.2 `apps/management-cameras/src/features/cameras/CameraViewerLifecycle.test.tsx`

- Line 8-9: replace the comment and `const store = createApiStore([camerasApi, streamsApi]);`
  with `createStore()` + `let store`.
- `beforeEach` (line 41-45) gains `store = createStore();` first. Real timers are active there:
  fake timers start only inside test 2 (line 75).
- Test 1's `rerender` (line 63) keeps using the same `store` within the test — unchanged meaning.

### 2.3 `apps/management-cameras/src/features/cameras/CameraViewerAlignment.test.tsx`

- Line 9-10: same replacement as 2.2.
- `beforeEach` (line 73-77): `store = createStore();` **before** `vi.useFakeTimers();` (line 76).
  This ordering is the one place FR-002 constrains placement.

### 2.4 Comment text (FR-005)

Each file's store comment becomes one line of *why*, no spec/task reference, e.g.:
*"A store per test: the auto-batch enhancer's notification latch lives in the store, so a
fake-timer window that strands its callback would otherwise silence every later test's
subscribers."* The camera files' pointer to `CamerasPage.test.tsx`'s comment is dropped — that
comment explains where `createApiStore` came from, not why the store is per test.

## 3. Verification protocol (4a → 4b → 5)

**The latch probe** (recipe; run in the package directory with `npx vitest run <path>` — the form
observed working on 2026-10-10). For a target file `F`, create **uncommitted**
`ZzLatchProbe.test.tsx` beside it as a copy of `F` with:

1. `import { SHOULD_AUTOBATCH } from '@reduxjs/toolkit';` prepended.
2. Immediately after the anchor line below (inside the first fake-clocked test), one line:
   `store.dispatch({ type: 'probe/under-fake-clock', meta: { [SHOULD_AUTOBATCH]: true } });`

   | File | Anchor (first occurrence) |
   |---|---|
   | `CameraViewerLifecycle` | `    expect(construct).toHaveBeenCalledTimes(2);` |
   | `CameraViewerAlignment` | `    expect(setPlayoutTarget).not.toHaveBeenCalled();` |
   | `OverlayDraftForm` | `    expect(screen.getByTestId('frame-capture-alert')).toHaveTextContent(/could not be captured/i);` |
3. Appended as the **last test of the last `describe`** (before its closing `});`):

   ```tsx
   it('PROBE: a later test still receives store notifications', async () => {
     vi.useRealTimers();
     const listener = vi.fn();
     const unsubscribe = store.subscribe(listener);
     store.dispatch({ type: 'probe/later', meta: { [SHOULD_AUTOBATCH]: true } });
     await new Promise((resolve) => setTimeout(resolve, 250));
     unsubscribe();
     expect(listener).toHaveBeenCalled();
   });
   ```

   (`vi.useRealTimers()` first, because Alignment's and *Frame capture*'s `beforeEach` fake the
   clock for this test too. 250 ms > the enhancer's 100 ms fallback.)

**Control**: the probe with step 2 omitted passes (observed for Lifecycle, `Tests 4 passed (4)`),
so the probe's red is the latch, not the probe.

1. **4a (`test-writer`)** — run each unmodified file once (characterisation baseline: 21/21, 3/3,
   2/2); build the probe from each file **as on `develop`**, run it, return verbatim output.
   Expected red, exactly the probe test, all three. **If any probe does not go red, stop and
   report** — the premise does not hold for that file and the plan must be revisited. Delete the
   probes.
2. **4b (`frontend-engineer`)** — apply §2, receiving step 1's output as its brief. May not touch an
   `expect(` line.
3. **4b/5, green half** — rebuild each probe from the **fixed** file (same anchors; the fixed
   `store` is the per-test binding), 10 runs each, all green. Delete the probes. Then 20
   consecutive runs of each fixed file, the full `management-web` and `management-cameras` suites,
   `prettier --check`, ESLint, `tsc --noEmit` for both packages.
4. **PR body** — quotes steps 1 and 3 verbatim, `git diff --stat`, and
   `git diff -U0 | grep -E '^[+-].*expect\('` returning nothing.

## 4. Commits (ADR-0030, one concern each; each builds and passes alone)

1. `fix(management-web): give OverlayDraftForm tests their own store`
2. `fix(management-cameras): give camera-viewer tests their own store` (both camera files)

No commit adds a red test, so no ADR-0087 bisect caveat applies.

## 5. Risks

- **FR-002 ordering is the only subtle point.** A store built under the fake clock would change
  how notifications flush during the test; review should check each `createStore()` call precedes
  the relevant `useFakeTimers()`.
- **Fresh worktree has no `node_modules`.** `pnpm install --frozen-lockfile` first (≈45 s observed).
- **Kiosk pair unaddressed** — follow-up issue for the orchestrator (spec *Edge Cases*).
