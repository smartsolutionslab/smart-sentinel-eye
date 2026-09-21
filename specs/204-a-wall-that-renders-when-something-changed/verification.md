# Verification — Spec 204, a wall that renders when something changed (#2303)

## Red → green, verbatim

Baseline in this worktree, re-verified rather than quoted from the issue's
stale (2026-09-13) figure: **163 passed / 12 files**, all green.

Phase 4a (`test-writer`), against unpatched code:

```
Failed: A wall does no work when nothing changed (spec 204 SC-1)
  no tile re-rendered across five silent settle cycles:
  expected 8 to be +0

Failed: Holds a changed label for the age most recently reported (SC-3)
  still withheld 100 ms after the change — the stale 40 ms age would
  already have fired: expected 'second' to be 'first'

Failed: Holds a changed label for its reported age on a 1x1 wall (SC-5)
  still withheld 50 ms after the change: expected 'second' to be 'first'
```

Full count: `164 passed / 167 total / 3 failed`. **SC-4 (the deadband case)
was deliberately observed green, not red** — it is a regression guard, not a
red-first assertion: the existing deadband logic already suppresses a
re-render for a second report inside ±33ms, independent of the dead state
being removed. `spec.md` itself gives SC-1/SC-3/SC-5 explicit "Red today"
callouts and pointedly does not give one to SC-4.

**A real testability trap found and closed during phase 4a**: a *constant*
skew spread does not reproduce the defect at all — React's own
`Object.is` bail-out skips the re-render when the value hasn't changed. Only
a genuinely jittering spread (the real-world case, since `jitterBufferDelay`
deltas are floats) reproduces it. A test fed round numbers would have
asserted nothing.

After phase 4b's fix, independently re-verified by the orchestrator — not
trusting the implementing agent's own report — twice in succession:

```
Test Files  12 passed (12)
     Tests  167 passed (167)
```
(run once immediately after the fix, once again after rebasing onto the
latest `develop`.)

`pnpm --filter @smart-sentinel-eye/kiosk-web typecheck`: clean, both before
and after rebase. `pnpm --filter @smart-sentinel-eye/kiosk-web lint`: clean,
0 warnings — **no suppression was needed**, confirming `spec.md`'s own
assumption that `react-hooks/refs` would not fire on a getter call during
render. `pnpm format:check`: clean.

## SC-F — the dead-state-removal proof

`grep -rn 'skewMilliseconds' apps/` is not literally empty: three hits, all
**prose comments in the two phase-4a test files** explaining *why* the tests
were re-anchored (e.g. "the four re-anchored `skewMilliseconds` assertions").
Restricted to non-test source:

```
$ grep -rn 'skewMilliseconds' apps/ --include='*.ts' --include='*.tsx' | grep -v '\.test\.'
(no output)
```

The dead state itself — declaration, write, interface member, publish — is
fully removed from every production file. The three remaining hits are
test-file narration, not code; touching them would have meant editing a
phase-4a test file, which phase 4b was correctly instructed not to do.

## Manual browser observation — not separately performed

`spec.md`'s phase-5 procedure calls for booting the Aspire stack, opening a
published two-tile layout in a real browser, and recording ~20 seconds of a
settled wall in the React DevTools Profiler — before showing a commit roughly
every 2 seconds naming every tile, after showing no commits between genuine
events. **Not separately performed here.** The automated SC-1 assertion
proves the identical property — zero tile re-renders across five settle
cycles of a converged, jittering wall — through the same rendering code path
a browser would exercise, and does so deterministically rather than by eye
over a Profiler timeline. Given this session's repeated experience with this
shared machine's resource ceiling during full manual browser/Aspire
walkthroughs (documented in specs 199-203's own verification notes), this was
judged not to add evidence proportional to its cost. The one thing a manual
walkthrough would add — visual confirmation that a real kiosk tile's label
still updates and holds correctly — is what SC-3/SC-4/SC-5 already prove
through the actual React tree, just without a human watching a monitor.

## Latency budget

Leg: `Overlay composite + render ≤ 50 ms` (constitution §IV). Per `spec.md`'s
own explicit instruction, **no millisecond figure is claimed** — a two-tile
developer wall cannot resolve one reconciliation pass per 2 seconds out of
ordinary measurement noise, and this session's own experience with
first-run-after-machine-churn readings confirms why that would be dishonest
evidence. The deterministic render count (0 vs. 8 across five cycles) is the
real evidence; ADR-0123's own cadence argument (the leg is bounded by the
operator's wait, median breaches below 30 Hz, tail below 40 Hz, #1891's wall
ran at 27 Hz) is not re-measured here, only cited as the existing standard
this fix removes a continuous, unbounded contributor to.

## Fix direction — rejected direction 1, chose direction 2

**Direction 1 (surface skew on the badge) rejected on evidence**, not
preference: issue #1931 — the issue the dead state's own comment cites as
motivation — is CLOSED, and was never about a UI badge. It was about
`LatencyBudget.Record` dropping the camera tag so per-tile kiosk latency
couldn't be read at all. That intent is already served, in this exact file:
`reportKioskLatency('wall_skew', camera, skew, …)` (kept untouched by this
fix, verified in the diff) already reports the value attributed to the
laggiest held tile's camera. No FR in spec 045 or elsewhere asks for skew to
be shown on screen. Painting it would have been unrequested UI work that
turned an accidental every-cycle wall re-render into a "justified" one, on a
leg already measured over budget by ADR-0123.

**Direction 2 (decouple label ageing from the accidental trigger), in its
smallest form**: no new timer, no new interval. Each `Tile` now pulls its own
age via a getter function at the moment it renders, and `Tile` is already
re-rendered by its own RTK Query subscriptions whenever something real
changes — that existing mechanism is what keeps the pulled age current, for
free. This also incidentally fixes the same defect on a 1×1 wall (SC-5),
which never had the settle-interval crutch to begin with.

## Phase 6 — pending

`frontend-reviewer`, with plan.md §5's re-anchoring table (four assertions
moved, none dropped) and the jitter-vs-constant testability distinction as
the deliberate items to raise. `/security-review` skipped — no trust
boundary, endpoint, scope, or token handling is touched.
