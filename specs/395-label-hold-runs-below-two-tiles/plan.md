# Plan — Spec 395, the label hold already runs below two tiles

- **Spec:** `spec.md` (verdict: **already fixed** by spec 204, `2fb7b5d0`)
- **ADRs:** ADR-0129, ADR-0128, ADR-0139, ADR-0144, ADR-0036. **No new ADR.**
- **Phase 4a colour: CHARACTERISATION (behaviour-preserving).** No product file
  changes. The only non-markdown file touched is a Playwright spec, and in it
  only comments and two `console.info` string literals that no assertion reads.

## 1. Bounded context and layers

None. `kiosk-web` presentation is *read* for evidence and not edited. No backend,
no `Shared.Contracts`, no Aspire resource, no migration. No cross-context rule is
in play.

## 2. Domain model

N/A — no entities, value objects or invariants change. The invariant that matters
is a frontend one and already holds and is already guarded:

> A tile's label hold is scheduled from the frame age current **at the tile's
> render that carries the text change**, on every wall size
> (`LayoutGrid.tsx:394`; guarded by `CellPage.test.tsx:2564` for 1×1, `:2412`
> and `:2489` for two tiles).

And the tile-count gate is **intentional and correct**: `useWallAlignment.ts:124`
gates only the *alignment* loop (spec 045 FR-004 — a single-tile wall adds no
delay). It is not the hold's dependency since `2fb7b5d0`.

## 3. Messaging

N/A.

## 4. The three edits

### E1 — `e2e/kiosk-shows-a-label-over-video.spec.ts:1114-1137` (comment + diagnostic literal)

- Comment: replace "re-verify this pairing after spec 204 rather than trust it"
  with the outcome — the fixture is the nine-tile wall (spec 262), on which CI
  run `39065520458` captured 90 `label_delay` samples (p50 59.5 ms); the one-tile
  case is guarded by `CellPage.test.tsx`'s 1×1 test (spec 204 SC-5); spec 395.
  Keep the history sentences (phase 4a's misattribution, spec 108's probes).
- Literal: drop "on this ONE-TILE wall"; say `no samples` on this fixture means
  no tile's label changed after its first lag sample arrived during the run,
  which on the nine-tile fixture is unexpected and worth reading. Keep "200 ms is
  the cap the budget PERMITS, never an observation".
- Do not change the `if` condition, its placement, or `reportLegs`'s signature.

### E2 — `e2e/kiosk-shows-a-label-over-video.spec.ts:992` (literal)

`one tile, one clip` → a literal that names both counts without a new runtime
value: `one observed tile (of the seeded wall), one clip`. **Not**
`${wall.cameras.length}` — that would be a new expression, i.e. a code change
the characterisation hash would rightly flag. *Advisory:* if the reviewer prefers
the interpolation, that is a separate behaviour-preserving change with its own
characterisation and should not be smuggled into this one.

### E3 — `specs/108-a-span-finer-than-its-budget/verification.md` (markdown)

Append a dated blockquote immediately after the three present-tense statements
(`:17-21` heading block, finding 1/2 at `:359-367`, the correction at `:647-651`):

> **Superseded 2026-10-17 (spec 395, #2876).** Fixed on 2026-09-21 by spec 204
> (`2fb7b5d0`, #2303 SC-5): each tile reads its own age at its own render. The
> e2e fixture is now nine tiles (spec 262) and CI captures the hold there.

No line of the original note is altered — other documents cite it by line.

## 5. Characterisation method (ADR-0139, comment-only → hash)

Per the repo's lesson *"characterise a comment-only change by hashing the
code"* — never assert that prose contains a string.

1. **Before any edit**, capture:
   - `H0` = hash of the e2e file with comments stripped and the two edited
     literals masked (strip `//…` and `/* … */` with a TypeScript-aware pass —
     `node` + the `typescript` package already in the workspace:
     `ts.createSourceFile` → print with `removeComments: true`; mask by replacing
     the two `console.info` argument expressions with a fixed token).
   - `L0` = `pnpm exec playwright test --list` output (baseline: **134 tests in
     44 files** at `c60c6252`).
   - `V0` = `cd apps/kiosk-web && npx vitest run` (baseline: **277 passed, 21 files**).
2. **Counterfactual for the hash** (the guard must be able to fail): change one
   non-comment token in a scratch copy (e.g. `'label_delay'` → `'label_delayX'`
   at the `lines.some` check) → hash must differ from `H0`. Record it.
3. **After the edits**: `H1 == H0`, `L1 == L0`, `V1 == V0`, and
   `git diff --stat origin/develop -- apps/ src/` empty.
4. `grep -rn "ONE-TILE\|one tile, one clip" e2e/ apps/` → no hits after the edit;
   `grep -rn "conditions:\|label_delay reading" e2e/ tests/ apps/ --include=*.ts`
   shows the literals are only ever printed, never asserted.

## 6. Gates the lane must run

- `pnpm format:check` (prettier) — the e2e file is formatted; memory note:
  eslint-clean is not prettier-clean.
- `pnpm lint` on the e2e workspace.
- `pnpm typecheck:e2e` — **known to fail on a clean develop** for a missing
  `@types/node` in the workspace root (memory note). Run it on `origin/develop`
  first; if it fails there identically, record that and do not "fix" config.

## 7. Risks

- **R1 — the lane "fixes" the gate.** Someone reads the issue title and removes
  `tileCount >= 2`. That would violate spec 045 FR-004 and change wall
  behaviour; out of scope, blocked.
- **R2 — the lane adds a one-tile e2e fixture.** New seed, new CI time, for a
  case vitest guards and the counterfactual proves. Out of scope (spec.md).
- **R3 — rewriting spec 108's note.** Append only.
