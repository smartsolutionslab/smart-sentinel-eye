# Tasks 140 — A sampler that fails loudly

**Spec:** [`spec.md`](spec.md) · **Plan:** [`plan.md`](plan.md) · **Issue:** #2189

Declared at phase 3: **behaviour-changing** → phase 4a is **red, observed failing**.
Engineer: **`frontend-engineer`**. **No ADR is needed** — the argument, and the condition
under which the gate should overrule it, are in spec §*Does this need an ADR?*

Everything below is **US-1**. US-2 (the healthy path stays silent) ships only as the
guard T008 and files nothing of its own.

**Parallelism (ADR-0109) is nil, and that is correct here.** Two files, and one is the
test for the other; phase 4a is strictly ordered before phase 4b by ADR-0144. The only
`[P]` markers are on the two evidence-gathering tasks that read the tree and change
nothing.

---

## Foundational — blocks everything

- [x] **T001** `[P]` `[US-1]` **Re-verify the defect is still at the cited lines.**
      Branches merge; the spec's line numbers are from 2026-09-13 and the issue's own
      were already stale by 50 lines.
      ```sh
      grep -n "catch" apps/shared/src/ui/composites/CameraViewer.tsx
      sed -n '158,190p;208,263p' apps/shared/src/ui/composites/CameraViewer.tsx
      git log --oneline -1 -- apps/shared/src/ui/composites/CameraViewer.tsx
      ```
      **Done when:** `:185` and `:258` are still `})().catch(() => undefined);`, `:288` is
      still the `setPlayoutTarget` catch, and the newest commit touching the file is the
      one phase 1 read. If any differs, **stop and re-plan** — do not adapt silently.

- [x] **T002** `[P]` `[US-1]` **Confirm no other branch has taken either file.**
      ```sh
      git fetch origin
      git log --all --oneline --name-only -- apps/shared/src/ui/composites/CameraViewer.tsx apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx | head -30
      ```
      **Done when:** no unmerged remote branch modifies either file. If one does, this
      becomes a stacked PR and the child must be retargeted to `develop` **before** the
      parent merges (CLAUDE.md §Stacked PRs).

---

## Phase 4a — tests first, and five of them must be seen failing

> **Agent: `test-writer`.** Writes tests only. Runs them. Returns the **verbatim**
> output, which goes in the PR body (ADR-0139). Does **not** touch `CameraViewer.tsx`.
> All cases go in `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx`, inside
> the existing `describe('CameraViewer when alignment fails')`, using its existing
> `resilienceLines()`, `flapThroughReconnect()` and `statsBehaviour` seam — see plan
> §*How the red is constructed*. **`CameraViewer.test.tsx` and `CameraViewerMedia.test.tsx`
> are not opened.**

- [x] **T003** `[US-1]` **R1 — `Says so when reading the tile lag throws`.** Default
      `statsThrows` double; render with `onLagMeasured={() => {}}`; advance 20 000 ms.
      Assert, **in this order**: `statsThrows` was called (a double never reached makes
      every later assertion true of a component that did nothing); then
      `resilienceLines('lag-sampler-failed')` has length **2**, payloads
      `{ subsystem: 'stream', transition: 'lag-sampler-failed', cameraIdentifier: 'cam-42', count: 1, reason: 'getStats exploded' }`
      and the same with `count: 10`; then the video element is still present.
      **Depends on:** T001. **Red today:** zero lines.

- [x] **T004** `[US-1]` **R2 — `Says so when the decode sampler throws on a page with no
      wall`.** Default throwing double; render **without** `onLagMeasured`; advance
      20 000 ms. Assert the double ran, then `resilienceLines('decode-sampler-failed')`
      has length **1** with `count: 1`, and `resilienceLines('lag-sampler-failed')` is
      empty — the lag interval never starts when nobody asked for it
      (`CameraViewer.tsx:204–207`). **Red today.**

- [x] **T005** `[US-1]` **R3 — `A sampler that fails does not silence the other`.** The
      case that a single shared counter would fail. Needs the **advancing** report factory
      from plan §*The one case that needs a new fixture*: `videoStatWithout()` returns
      constants, so `lagBetween`/`bufferDelayBetween` answer null and `onLagMeasured` is
      never reached. Set `statsBehaviour` to that factory, pass an `onLagMeasured` that
      throws, advance 20 000 ms. Assert **`onLagMeasured` was actually called** (else the
      case is vacuous), then ≥ 1 `lag-sampler-failed` line and **zero**
      `decode-sampler-failed` lines. **Red today.**

- [x] **T006** `[US-1]` **R4 — `Bounds a permanently broken sampler to a decade
      cadence`.** Default throwing double, `onLagMeasured` supplied, advance
      **200 000 ms** (100 lag ticks). Assert exactly **3** `lag-sampler-failed` lines with
      `count` 1, 10, 100 in order. This is the case a log-every-tick fix fails.
      **Red today.**

- [x] **T007** `[US-1]` **R5 — `Counts across a flap rather than starting again`.**
      Default throwing double; advance 20 000 ms (2 lines, counts 1 and 10); record the
      double's call count; call `flapThroughReconnect()`; advance 20 000 ms again. Assert
      a **second session really happened** (`sessions.length` grew) **and the double was
      called more times than before the flap** — otherwise "no new line" is true of a
      component that never sampled again — then that the total is **still exactly 2**
      lines: the second window carries the counter past 10 and the next boundary is 100.
      Fails against an effect-scoped counter, which would restart at 1 and emit a third
      line. **Assert on the totals, not on a tick count during the flap** — the 5 s
      disconnect grace may or may not leave the sampler running, and the case must not
      depend on which. **Red today.**

- [x] **T008** `[US-1]` **G1 — `Says nothing about a sampler that does not throw`
      (GREEN before and after).** The advancing factory from T005, a plain
      `onLagMeasured` that returns, advance 20 000 ms. Assert the double ran **and**
      `onLagMeasured` was called, then **zero** lines of either transition. Green today
      because nothing logs; it exists so the fix cannot be an unconditional
      `logResilienceEvent`, which would claim a broken instrument on every healthy kiosk
      with the whole suite green. **Not counted as a red** (ADR-0139).

- [x] **T009** `[US-1]` **Run the suite and capture the verbatim failure.**
      ```sh
      pnpm --filter @smart-sentinel-eye/shared test -- CameraViewerAlignment
      ```
      **Done when:** T003–T007 are observed **failing**, T008 and the six pre-existing
      cases in the file are observed **passing**, and the raw output is returned to the
      orchestrator for the PR body. **If any of T003–T007 passes, stop** — the test is
      not exercising the defect, and adjusting the implementation to suit it is the
      shortcut the gate exists to prevent.

---

## Phase 4b — the implementation

> **Agent: `frontend-engineer`.** Receives T009's verbatim output as its brief. **May not
> edit the tests to make them pass.**

- [x] **T010** `[US-1]` **Add the two module-private helpers** `countReportableFailure`
      and `reasonFrom` at the bottom of
      `apps/shared/src/ui/composites/CameraViewer.tsx`, beside `labelFor`, with the doc
      comment from plan §*Edit 1* — including the sentence naming #2084 as the source of
      the cadence and the deliberate duplication. **Blocks:** T011.
      **Done when:** `pnpm --filter @smart-sentinel-eye/shared typecheck` is clean. The
      tests are still red; this task adds no call site.

- [x] **T011** `[US-1]` **Add the two counters and the reporter** at component scope
      immediately after `reportMissingStatsField` (`:145`), with the comment from plan
      §*Edit 2*. Two `useRef(0)`, one `useCallback` on `[cameraIdentifier]`.
      **Do not modify `reportedMissingFieldsRef` or `reportMissingStatsField`** — the new
      mechanism sits beside them (FR-008). **Blocks:** T012, T013.

- [x] **T012** `[US-1]` **Replace the decode catch at `:185`** with
      `.catch((error: unknown) => reportSamplerFailure(decodeSampleFailuresRef, 'decode-sampler-failed', error))`
      and add `reportSamplerFailure` to that effect's dependency array.
      **Done when:** T004's case passes and the `react-hooks/exhaustive-deps` lint rule
      is silent.

- [x] **T013** `[US-1]` **Replace the lag catch at `:258`** the same way, with
      `lagSampleFailuresRef` and `'lag-sampler-failed'`, plus the dependency.
      **Done when:** T003, T005, T006, T007 pass.

- [x] **T014** `[US-1]` **Confirm the three untouched catches are untouched.**
      ```sh
      git diff -- apps/shared/src/observability/kioskLatency.ts
      sed -n '276,306p' apps/shared/src/ui/composites/CameraViewer.tsx
      git diff --stat
      ```
      **Done when:** `kioskLatency.ts` shows **no diff at all** (FR-010), the
      `setPlayoutTarget` try/catch and its `!applied` report are unchanged, and
      `git diff --stat` names exactly **two** files plus the three `specs/140-…` files.

---

## Verification and gates

- [x] **T015** `[US-1]` **Whole-package green, and the characterisation intact.**
      ```sh
      pnpm --filter @smart-sentinel-eye/shared test
      pnpm --filter @smart-sentinel-eye/shared lint
      pnpm --filter @smart-sentinel-eye/shared typecheck
      git diff --stat -- apps/shared/src/ui/composites/CameraViewer.test.tsx
      ```
      **Done when:** every test in the package passes, lint and typecheck are clean, and
      **`CameraViewer.test.tsx` shows no diff** (NFR-001 — its `toEqual` over the complete
      `[resilience]` array is the tripwire for a leaked line).

- [x] **T016** `[US-1]` **Prettier.** `pnpm format:check` clean for the two changed files.

- [ ] **T017** `[US-1]` **Phase 5 — observe it in a browser**, following spec
      §*Independent end-to-end test procedure*. A `console.info` is observable in the real
      thing, so a green unit suite is not the discharge. **Done when:** the four
      observations (first decode line, first lag line, the `count: 10` line, video still
      playing throughout) are recorded verbatim in the verification note on the PR.
      **Cite the latency budget:** no leg moves; the change is on the failure branch of
      two observers that run at 5 s and 2 s.

- [x] **T018** `[US-1]` **Phase 6 — `/code-review`.** `frontend-reviewer`. Point it at
      spec §*Decision* 2 and 3 — the two-counters argument and the recorded duplication
      are the two findings most likely to be re-derived from scratch.

- [ ] **T019** `[US-1]` **Phase 7 — PR to `develop`** (`gh pr create --base develop`),
      body quoting T009's verbatim red output (ADR-0139) and carrying
      `Closes #2189`. Conventional Commits, **no `Co-Authored-By`** (ADR-0086).

- [x] **T020** `[US-1]` **File the deferred item 3 as its own issue** — spec
      §*Decision* 5 — and add it and #2189 to Project #13:
      ```sh
      gh project item-add 13 --owner smartsolutionslab --url <issue-url>
      ```
      Title: *"A kiosk sampler that has stopped measuring should be visible at the sink,
      not only in the tile's console"*, referencing #2189, #1714, ADR-0122, ADR-0117, and
      stating that it likely needs an ADR because it extends the browser→service
      telemetry contract — **which the autonomous lane may not write** (ADR-0144).

---

## Dependency graph

```
T001 ─┐
T002 ─┴→ T003 ─┐
         T004 ─┤
         T005 ─┼→ T009 ─→ T010 ─→ T011 ─┬→ T012 ─┐
         T006 ─┤                        └→ T013 ─┴→ T014 → T015 → T016 → T017 → T018 → T019 → T020
         T007 ─┤
         T008 ─┘
```

T003–T008 are one file and one agent's turn, so they are written together rather than in
parallel. T012 and T013 touch two regions of one file and are ordered, not `[P]`.
