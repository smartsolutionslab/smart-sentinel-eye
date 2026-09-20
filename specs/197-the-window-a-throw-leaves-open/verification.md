# Verification 197 — The window a throw leaves open (#2314)

**Latency: constitution §IV, presentation-buffer and decode legs.** No leg's
duration changes and neither row moves in §IV's table (presentation-buffer
stays "recorded, not yet observed"; decode stays "in part") — this fix
changes only the fidelity of the figure reported after a sampler failure,
not whether the leg has been measured against a live wall.

## Summary

`apps/shared/src/ui/composites/CameraViewer.tsx`'s two lag samplers
(callback and decode-stats) advanced `previous` only on a successful tick.
The original issue framed the risk as "one widened window" — arithmetically
harmless, since the per-frame-mean math tolerates a single widened sample
fine. The real bug, found during this delivery's own investigation: under
a **persistent** throw, `previous` never advances, the window grows
unbounded, and the first successful tick after recovery reports a
per-frame mean flattening the entire outage into a falsely-normal figure —
directly the "session average flattens the excursion a budget is about"
failure this repo's own `wallAlignment.ts` comments warn against. Fixed
with `previous = null;` inside each sampler's existing `.catch` (two
sites), as a sibling statement before the existing `reportSamplerFailure`
call from #2189 — never restructuring that behavior.

## Phase 4a — real, naturally-occurring red (ADR-0139), quoted verbatim

```
FAIL  Reports the fresh-window figure, not the widened blend, after a one-off callback throw
  expected 2.500000000000006 to be close to 4
FAIL  Does not accumulate a window under a persistently throwing callback
  expected 3.727272727272727 to be close to 4
FAIL  Recovers a fresh window the same way when getStats — not our code — throws
  expected 2.500000000000006 to be close to 4
FAIL  Reports the fresh-window figure, not the widened blend, after a getStats throw on the decode sampler
  expected 2.9999999999999996 to be close to 4.5
Test Files  1 failed (1)
     Tests  4 failed | 1 passed (5)
```

Independently reproduced by the orchestrator (4/5 matching exactly) and
again by the phase-6 reviewer, who additionally hand-computed the expected
values from first principles (frame counts × per-frame coefficients before
and after a deliberate rate step) and confirmed every one of the four
observed pre-fix figures to the digit — the discrepancy is 1.5ms or more
against a `toBeCloseTo(_, 5)` tolerance of 5e-6, six orders of magnitude
too tight to be floating-point noise.

## The fix

```diff
       })().catch((error: unknown) => {
+        previous = null;
         reportSamplerFailure(decodeSampleFailuresRef, 'decode-sampler-failed', error);
       });
...
       })().catch((error: unknown) => {
+        previous = null;
         reportSamplerFailure(lagSampleFailuresRef, 'lag-sampler-failed', error);
       });
```

Two sites, one line each. `#2189`'s loud-failure reporting and its
callback-vs-`getStats` distinguishability are structurally untouched — the
same counter refs, the same transition literals, the same errors, reset
placed before the existing call so a throw from `reportSamplerFailure`
itself would still leave `previous` reset.

## Phase 6 — one review round, one blocker, one should-fix, both fixed

`frontend-reviewer` independently reproduced the entire red-then-green
sequence (checking out the pre-fix source, confirming the same 4 failures,
restoring), verified the fixture design avoids both traps by reading it
directly (stepped, not constant, per-frame rate; keyed on `Date.now()`,
confirmed absent from the fixture entirely), and hand-derived the correct
answer for every one of the four test cases independently.

**Blocker, fixed**: the new test file had one Prettier formatting
deviation, which fails CI's `frontend` job at its very first step
(`format:check`, before Lint/Typecheck/Test even run) — a real,
CI-blocking finding caught before the PR opened rather than after. Root
cause: `tasks.md`'s own "Full frontend gate" checklist never mentioned
Prettier at all. Fixed the formatting and added `pnpm format:check` as the
first line of that checklist so the next spec's gate matches CI's own
order.

**Should-fix, fixed**: a `Co-Authored-By` footer had leaked onto the
initial docs commit (ADR-0086 forbids it unconditionally) — stripped via
rebase, tree content confirmed unchanged.

**Two nice-to-haves, both folded in**:
- A second-order consequence the reviewer found and confirmed
  empirically: since a seeding tick can never throw, `previous = null`
  halves how often a persistently-throwing callback's own failure counter
  actually advances — #2189's decade-cadence line now lands roughly every
  other tick instead of every tick. Documented in the fix's own comment.
- spec.md's stated reason for leaving the two non-throwing early returns
  un-reset was wrong ("drops a sample on every mount" — `previous` is
  already `null` at mount, so a reset there is a no-op). Corrected to the
  accurate distinction: one sub-case (a missing stat field) is genuinely
  benign and already instrumented; the other (no inbound-video stat at
  all, or the WHEP client vanishing under a still-`live` status) has no
  instrument at all and is the one genuinely uninstrumented corner —
  correctly out of scope here, but named accurately for whoever follows up.

## Independent re-verification, this pass

```
$ npx vitest run   (apps/shared, full suite)
Test Files  32 passed (32)
     Tests  407 passed (407)

$ npx prettier --check "{apps,e2e}/**/*.{ts,tsx,js,jsx,json,css,md,yaml,yml}" playwright.config.ts
All matched files use Prettier code style!

$ npx eslint src --max-warnings 0
(clean, no output)

$ npx tsc --noEmit
(clean, no output)
```

`CameraViewerAlignment.test.tsx` (the pre-existing #2189 regression suite)
confirmed byte-identical to `origin/develop` throughout.

## Constitution §IV — confirmed unaffected

§IV's rigor is about the strength of a *claim* ("an estimate is not a
measurement"; "a leg marked measured on the strength of a passing unit
test is a discharge nobody earned"), not about per-sample cadence. Nobody
stood in front of a real wall for this fix, so neither the
presentation-buffer nor decode row moves in §IV's table — this fix
improves the fidelity of what gets reported after a failure, it does not
newly measure a leg. Both spec.md and plan.md state this explicitly and
the phase-6 reviewer independently confirmed the reasoning against the
constitution's actual text.
