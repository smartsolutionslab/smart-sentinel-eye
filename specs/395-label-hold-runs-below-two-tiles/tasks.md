# Tasks — Spec 395, the label hold already runs below two tiles

- **Issue:** #2876 · **Branch:** `fix/2876-label-hold-below-two-tiles` · **Worktree:** `D:/Github/sse-2876`
- **Base:** `origin/develop` @ `c60c6252`
- **Baseline:** kiosk vitest **277 passed / 21 files**; Playwright `--list` **134 tests in 44 files**
- **Phase 4a colour: CHARACTERISATION.** Behaviour-preserving: no product file
  changes; the e2e file changes in comments and two printed literals only. The
  characterisation is a **hash of the comment-stripped, literal-masked file**
  plus the unchanged test lists — captured green *before* the edit, identical
  *after*. Any hash difference is a block, not an adjustment.
- **Engineer:** `frontend-engineer` (e2e TypeScript + markdown).

## Parallelism (ADR-0109)

T004 (markdown) is disjoint from T003 (e2e file) → `[P]`. T001–T002 block both.

```
T001 ── T002 ──┬── T003 ──┬── T005 ── T006 (optional)
               └── T004 ──┘
              (T003 [P] T004)
```

## Phase 4a — characterisation (`test-writer`)

| ID | [P] | Story | Task |
|---|---|---|---|
| **T001** | | US1 | Baseline. `pnpm install --frozen-lockfile`; `cd apps/kiosk-web && npx vitest run` → record totals (expect 277/21); `npx vitest run src/features/cell/CellPage.test.tsx -t "most recently reported"` → expect 3 passed; `pnpm exec playwright test --list > L0.txt` at repo root (expect 134/44). Scratch files outside the repo. |
| **T002** | | US1 | Hash characterisation script (scratchpad, **not committed**): node + workspace `typescript`; parse `e2e/kiosk-shows-a-label-over-video.spec.ts`, mask the argument of the `console.info` at `:992` and the one inside `if (!lines.some((line) => line.measurement === 'label_delay'))` at `:1128-1137`, print with `removeComments: true`, sha256 → `H0`. **Counterfactual, required:** in a scratch copy change `'label_delay'` in that `lines.some` condition to `'label_delayX'` → hash ≠ `H0`; and edit only a comment → hash = `H0`. Return verbatim output of all three. Also re-run the spec-204 counterfactual from spec.md Evidence 2 (patch `LayoutGrid.tsx:257`, observe SC-5 red, `git checkout --`) and return the verbatim output — it is this spec's proof that the one-tile case is guarded. |

**Gate 4a:** T001 totals, `H0`, both hash counterfactuals and the SC-5
counterfactual output quoted verbatim in the PR body.

## Phase 4b — the edits (`frontend-engineer`). May not edit any test assertion or the T002 script.

| ID | [P] | Story | Task |
|---|---|---|---|
| **T003** | `[P]` | US1 | `e2e/kiosk-shows-a-label-over-video.spec.ts` per plan §4 E1 + E2: rewrite the comment at `:1114-1127` (outcome of the re-verification, spec 395, CI run `39065520458`: 90 samples, p50 59.5 ms, nine-tile fixture; 1×1 guarded by `CellPage.test.tsx` "…on a 1x1 wall, which never settles"); replace the diagnostic literal's "on this ONE-TILE wall" framing; `:992` `one tile, one clip` → `one observed tile (of the seeded wall), one clip`. No change to any expression, condition or call. |
| **T004** | `[P]` | US1 | `specs/108-a-span-finer-than-its-budget/verification.md` per plan §4 E3: three dated "Superseded 2026-10-17 (spec 395, #2876)" blockquotes appended after `:17-21`'s heading block, after finding 2 (`:367`), after the correction at `:651`. **Append only** — `git diff` must show additions and zero deletions in this file. |

## Phase 4 close / phase 5

| ID | [P] | Story | Task |
|---|---|---|---|
| **T005** | | US1 | Re-run T001 and T002 against the edited tree: `H1 == H0`, `L1 == L0` (diff empty), vitest 277/21, the three label-age tests green **unmodified**; `git diff --stat origin/develop -- apps/ src/` empty; `git diff --numstat -- specs/108-*/verification.md` shows 0 deletions; `grep -rn "ONE-TILE" e2e/` empty; `pnpm format:check`; `pnpm lint`; `pnpm typecheck:e2e` (if it fails, run it on clean `origin/develop` first — known pre-existing failure — and record both outputs). |
| **T006** | | US1 | *Optional (phase 5), not a gate.* One-tile live observation: a **throwaway** probe (not committed, no product change) seeding a 1×1 wall on the Aspire stack, soaking ≥ 45 s, driving ≥ 5 value changes, recording `label_delay` sample count and values — the spec 108 probe shape. Before (pre-204): 0 samples. Expected after: ≥ 5 samples. If skipped, the PR body says so and why (one machine, one Aspire stack). If it reports **0 samples**, **stop**: the verdict is wrong and the issue becomes a real gap. |

## Commit plan (ADR-0030, no `Co-Authored-By`)

1. `docs(395): …` — this spec (already committed in phase 1–3).
2. `docs(2876): the e2e harness stops calling its nine-tile wall one tile` — T003.
3. `docs(2876): mark spec 108's one-tile label-hold finding superseded by spec 204` — T004.

Each builds alone; none touches product code.
