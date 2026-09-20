# Verification 198 — The match two absences made (#2320)

**Latency: N/A.** Cross-fab isolation fix; no leg of constitution §IV's
event→overlay path touched.

## Summary

`CellPage.tsx`'s two fab-comparison guards (`if (message.fab !== wallFab)
return;`) wrongly let a push through when both sides were absent — either
both `undefined`, both empty string, or (found during phase 6 review,
extending this delivery beyond the original issue) both whitespace-only.
Once a wall runs against a fab-less layout, any push from any other fab
whose own `fab` field happens to be missing gets silently accepted — the
same cross-fab leak class as #2069, reached through a different door.
Fixed with `if (namedFab(wallFab) === null || message.fab !== wallFab)
return;` at both sites, and `namedFab` itself hardened to reject
whitespace, not just empty string.

## Phase 4a — real, naturally-occurring red (ADR-0139), quoted verbatim

```
FAIL A fab-less highlight frame lights a tile on a fab-less wall
  expected [ 'true' ] to deeply equal [ 'false' ]
FAIL A fab-less resolved-text push writes a snapshot cache entry a fab-less wall never asked for
  expected { …(3) } to be undefined
FAIL The empty-string twin lights a tile on an empty-string wall exactly like the undefined twin
  expected [ 'true' ] to deeply equal [ 'false' ]
FAIL The empty-string twin writes a cache entry a naive undefined-only guard would not have caught
  expected { …(3) } to be undefined
Total: 47  Passed: 43  Failed: 4
```

Independently reproduced by the orchestrator (4/47 matching exactly).

**Deliberate design choice, verified**: the primary red is the HIGHLIGHT
route, not the resolved-text route — on a fab-less wall the snapshot query
is already skipped by a prior fix (spec 141), so a red test asserting the
rendered label would pass today for the wrong reason. The highlight route
has no such skip, so it demonstrates the live defect directly.

## Phase 4b — the fix

```diff
-      if (message.fab !== wallFab) return;
+      if (namedFab(wallFab) === null || message.fab !== wallFab) return;
```
at both sites (resolved-text, highlight).

## Phase 6 — two review rounds, one blocker, security gap found and closed

`frontend-reviewer` and `security-reviewer` ran in parallel. No blocker in
the fix's core logic — both independently reproduced the red-then-green
sequence and hand-verified the guard's truth table. But:

**BLOCKER, fixed**: a `Co-Authored-By` footer had leaked onto the initial
docs commit — stripped via rebase, tree content confirmed unchanged.

**Security should-fix, fixed — the review's central finding**: `namedFab`
rejected empty string but not whitespace-only strings (`' '`, `'\t'`).
Independently confirmed against the actual server-side code
(`src/ServiceDefaults/Authorization/FabResolution.cs:60,103` uses
`!string.IsNullOrWhiteSpace(fabId)`) that the client and server disagreed
on what counts as "no fab" — a whitespace-only `wallFab` would also fail
to trigger the snapshot-query skip at `CellPage.tsx:471`, sending an
unconditional, untrimmed `fabId` to a server endpoint that then resolves
across every fab the caller holds — the #2069 leak again, reached via a
whitespace value. Fixed: `namedFab` now does
`fab.trim() !== '' ? fab : null` (returning the untrimmed value, since no
legal fab contains whitespace, so trimming the return value would only
mask a real mismatch elsewhere).

**Two should-fixes, fixed**: three stale comments in `CellPage.tsx` still
described the defect as open or argued the exact false premise this spec
disproves — reworded to describe the actual current guard behavior. The
verification command recorded in all three spec documents didn't
actually work in this repo (`npm --workspace` on a pnpm-only repo) —
corrected to `pnpm --filter ./apps/kiosk-web exec vitest run
src/features/cell/CellPage.test.tsx` throughout.

**One residual the fix-round agent explicitly flagged rather than
silently absorbing**: no dedicated red-then-green test existed for the
whitespace-fab fix itself. Added directly by the orchestrator — two tests
mirroring the existing empty-string twins, and proven real by
counterfactual:

```
$ sed -i "s/fab.trim() !== ''/fab !== ''/" CellPage.tsx   # revert just the fix
$ vitest run CellPage.test.tsx
Tests  2 failed | 47 passed (49)   # exactly the two new tests, nothing else
$ git checkout -- CellPage.tsx     # restore
$ vitest run CellPage.test.tsx
Tests  49 passed (49)
```

## Independent re-verification, this pass

```
$ pnpm --filter ./apps/kiosk-web exec vitest run src/features/cell/CellPage.test.tsx
Test Files  1 passed (1)
     Tests  49 passed (49)

$ pnpm --filter ./apps/kiosk-web run lint
(clean)

$ pnpm --filter ./apps/kiosk-web exec tsc --noEmit
(clean)

$ pnpm exec prettier --check "{apps,e2e}/**/*.{ts,tsx,js,jsx,json,css,md,yaml,yml}" playwright.config.ts
All matched files use Prettier code style!
```

`countReportableSkew` confirmed untouched throughout both fix rounds —
its own `wallFab === undefined` early return (not routed through
`namedFab`) is a deliberate, documented residual (SC-6): a whitespace or
empty `wallFab` still lets it log noise, which is a false-positive report,
not a data leak, and is explicitly out of this spec's scope.

## What the security review checked and closed, for the record

Full truth table walked by hand and by counterfactual execution: both
real/equal (applied, correct), both real/different (rejected), one real
one absent (rejected), both absent in every combination of `undefined`,
`''`, and whitespace (all rejected after the fix). Type coercion checked
(strict `!==` throughout, no `==`). Case sensitivity checked (matches the
server's `Ordinal` comparison in `FabIdentifier`). No third call site with
the same shape found elsewhere in the frontend. No stale-closure/timing
gap found (`optionsRef` kept current per-event, and a layout's fab is
immutable server-side once minted).
