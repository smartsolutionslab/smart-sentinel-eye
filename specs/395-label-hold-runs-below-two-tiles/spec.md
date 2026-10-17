# Spec 395 — The label hold already runs below two tiles

- **Issue:** #2876 — *"ADR-0129's label-hold is structurally absent below two tiles"*
  (`agent:ready`, enrolled as an investigation, owner decision 2026-10-17)
- **Branch:** `fix/2876-label-hold-below-two-tiles` · **Worktree:** `D:/Github/sse-2876`
- **Base:** `origin/develop` @ `c60c6252`
- **ADRs:** ADR-0129 (a label is *aged* to match its picture — the property in
  question), ADR-0128 (playout alignment; the settle loop), ADR-0037 (workflow),
  ADR-0144 (autonomous lane — phase 1 is the investigation), ADR-0139 /
  constitution §Testing (behaviour-preserving → characterisation), ADR-0036
  (smallest change). Spec 045 FR-004, spec 046 FR-011, spec 108, spec 204.
- **No ADR and no constitution change is needed.** Nothing about how a label is
  aged, how a wall aligns, or what §IV records changes. The constitution's §IV
  table makes no claim about the label hold below two tiles (`grep -n "label
  hold\|label_delay" .specify/memory/constitution.md` → no hits).

## Verdict: **already fixed** — the issue was filed 21 days after the fix

| Date | Event |
|---|---|
| 2026-09-09 | Spec 108 phase 5 finds the hold does not engage on a 1×1 wall (`specs/108-a-span-finer-than-its-budget/verification.md:17-74, :357-367`). Recorded in #1714's comment. |
| 2026-09-21 | Spec 204 (#2303, closed 2026-09-21) fixes it in `2fb7b5d0` — *"stop re-rendering the wall every cycle for a value nobody paints"*. Its **SC-5** is exactly this case: *"a one-tile wall ages its label too"*, observed red, then green (`specs/204-.../verification.md:19`, `:120`). |
| 2026-10-12 | #2876 is filed, transcribing the 2026-09-09 bullet from #1714 verbatim. |

The issue's mechanism sentence — *"`frameAgeFor` reads a ref during `CellPage`'s
render"* — describes the code **before** `2fb7b5d0`. It is not true on develop.

### Premise check against `c60c6252`

| Claim in #2876 | Verdict |
|---|---|
| "`frameAgeFor` reads a ref during `CellPage`'s render" | **Stale.** Since `2fb7b5d0` each `Tile` calls the getter in its *own* render: `apps/kiosk-web/src/features/cell/LayoutGrid.tsx:394` (`const frameAgeMilliseconds = frameAgeFor(tileKey);`), passed as a getter at `:257`. `Tile` is re-rendered by its own RTK Query subscription when its label text changes — the push path writes that cache (`useOverlayHubHandlers.ts:197`, `upsertQueryData`) — so the age is read at the moment the hold is scheduled. |
| "below two tiles the settle interval never runs" | **True, and intentional** — but it no longer touches the hold. `useWallAlignment.ts:124` `const aligning = tileCount >= 2;`, `:145-164` returns early. The gate is spec 045 **FR-004** (*"A single-tile wall MUST behave as it does today, adding no delay it did not previously have"*), stated in the hook's own doc at `:76-78`. `frameAgeFor` (`:273`) is gated on nothing, and stale-sample pruning runs on every wall size since #2498 (`:126-143`). |
| "so the tile keeps its mount-time `null`" | **False since `2fb7b5d0`** — proven below by test and counterfactual. |
| "Measured directly on a two-tile fixture: label age is 35–45 ms" | **Misread.** In spec 108's paired probes the two-tile wall was the *control*, where the hold **worked** (10 samples, 35–45 ms); the one-tile wall gave **0 samples** (`verification.md:50-71`). 35–45 ms was never the defective configuration's figure. |
| "Spec 108 US2's acceptance scenario is unreachable on its own fixture" | **No longer applicable.** The fixture is no longer one tile: spec 225 US2 / spec 262 grew it to a **3×3, nine-tile** wall (`e2e/support/seed-live-video-wall.setup.ts:23-29, :118`). On it, CI captures `label_delay` (below). |

### Evidence — commands and verbatim output

**1. The spec-204 guard is green on develop** (worktree at `c60c6252`, after
`pnpm install --frozen-lockfile`):

```
$ cd apps/kiosk-web && npx vitest run src/features/cell/CellPage.test.tsx -t "most recently reported"
 Test Files  1 passed (1)
      Tests  3 passed | 63 skipped (66)
```

The three are `CellPage.test.tsx:2412` (SC-3), `:2489` (SC-4) and
`:2564` *"Holds a changed label for its reported age on a 1x1 wall, which never
settles"* (SC-5) — a 1×1 layout, a real `useWallAlignment`, a real RTK cache,
a 150 ms lag, then a text change: label still old at 50 ms, new by 250 ms.

**2. The guard can fail — counterfactual.** Re-introduce the pre-204 shape (age
frozen at the parent's render) by patching `LayoutGrid.tsx:257` to
`frameAgeFor={((frozen) => () => frozen)(alignment.frameAgeFor(item.key))}`,
run the same filter, then `git checkout --` the file:

```
       × Holds a changed label for the age most recently reported, not the age its parent last rendered with 2199ms
       × Holds a changed label for its reported age on a 1x1 wall, which never settles 63ms
 FAIL  ... > Holds a changed label for its reported age on a 1x1 wall, which never settles
AssertionError: still withheld 50 ms after the change: expected 'second' to be 'first' // Object.is equality
      Tests  2 failed | 1 passed | 63 skipped (66)
```

So the 1×1 test detects exactly the mechanism #2876 describes, and it is green
today. (SC-4 stays green under this patch because a converged two-tile wall
still re-renders on its `setTarget`; it guards a different coupling.)

**3. The full kiosk suite** at `c60c6252`: `Test Files 21 passed (21)`,
`Tests 277 passed (277)`.

**4. CI captures the hold on the e2e fixture.** Last green develop run
`39065520458` (`86d32918`, 2026-10-17), e2e job `117494282552`
(`gh run view 39065520458 --job 117494282552 --log | grep '\[legs\]'`):

```
[span] conditions: linux, CI=true, one tile, one clip
[legs] presentation_buffer: 27 sample(s), p50 19.2 ms, max 26.1 ms — budget 200 ms (section IV presentation buffer)
[legs] wall_skew: 3 sample(s), p50 21.3 ms, max 22.7 ms — bound 33 ms (ADR-0128 section 2, intra-wall)
[legs] label_delay: 90 sample(s), p50 59.5 ms, max 91.7 ms — cap 200 ms (ADR-0129 hold — not a section IV leg)
```

`wall_skew` having samples proves this wall is aligning (≥ 2 tiles) — it is the
nine-tile fixture. Spec 108 US2 scenario 1 (*"`[latency]` lines for at least
`overlay_draw`, `presentation_buffer` and `label_delay`"*) is met on it.

### What has NOT been observed (disclosed, not claimed)

**No live browser has shown a held label on a one-tile wall since `2fb7b5d0`.**
The e2e fixture is nine tiles, so CI's 90 samples are multi-tile evidence. The
one-tile proof is the vitest SC-5 test (real hook, real RTK cache, mocked
`CameraViewer`), shown able to fail by the counterfactual above. Spec 108's own
note asked for this re-verification (`e2e/kiosk-shows-a-label-over-video.spec.ts:1126-1127`:
*"re-verify this pairing after spec 204 rather than trust it"*). A throwaway
one-tile probe like spec 108's is the way to discharge it; it is phase 5's
optional item (`tasks.md` T006), not a precondition of this verdict.

## What remains: three records that still say otherwise

The behaviour is fixed. The record is not, and a stale record is how #2876 was
filed three weeks after the fix (CLAUDE.md: *"a record nobody checked against
what was actually happening"*).

1. **`e2e/kiosk-shows-a-label-over-video.spec.ts:1128-1137`** — the
   `label_delay` no-samples diagnostic says *"on this ONE-TILE wall"*. The
   fixture has been nine tiles since spec 262. The comment above it
   (`:1114-1127`) carries the "re-verify after spec 204" instruction that this
   spec discharges for the multi-tile fixture.
2. **`e2e/kiosk-shows-a-label-over-video.spec.ts:992`** — `[span] conditions:
   …, one tile, one clip`. The span observes one tile's label (`.first()`,
   `:1275`) on a nine-tile wall; "one tile" reads as the wall's size, which is
   how a reader of CI output would reach #2876's premise. *Guess, marked:* the
   author meant "one observed tile"; the correction names both.
3. **`specs/108-a-span-finer-than-its-budget/verification.md`** — the
   "Read this first" section (`:17-74`), finding 1/2 (`:357-367`) and the
   correction at `:647-651` state the defect in the present tense. They are a
   dated record and stay as written; each gets a dated **superseded** pointer to
   spec 204 and this spec, not a rewrite.

## User stories

### US1 (P1) — A reader of the record is not told the hold is absent

*As the next investigator,* when I read the e2e harness's output or spec 108's
note, I am told the hold's actual state — fixed for one tile by spec 204, and
observed on the nine-tile fixture — rather than a 2026-09-09 diagnosis.

Independently shippable: three text edits, no product file touched.

## Acceptance scenarios

### SC-1 — the product is unchanged (characterisation)

```gherkin
Given the kiosk source tree at c60c6252
 When this spec's changes are applied
 Then `git diff --stat origin/develop -- apps/ src/` is empty
  And the 277 kiosk vitest tests pass unmodified
  And the three spec-204 label-age tests pass unmodified
```

### SC-2 — the e2e file changes only in comments and two printed strings

```gherkin
Given e2e/kiosk-shows-a-label-over-video.spec.ts before and after
 When every comment is stripped and the two edited string literals are masked
 Then the two hashes are identical
  And `pnpm exec playwright test --list` lists the same tests in the same order
  And no assertion anywhere reads either string (grep)
```

### SC-3 — the record matches the code (bad-request analogue: a false claim is refused)

```gherkin
Given the repository after this spec
 When a reader greps for "ONE-TILE" in e2e/ and for the present-tense defect in spec 108's note
 Then no line states the e2e fixture is one tile
  And each spec-108 statement of the defect is followed by a dated pointer to spec 204 and spec 395
```

### SC-4 — auth

N/A, stated rather than omitted: no endpoint, scope, token or trust boundary is
touched.

## Independent end-to-end test procedure

1. `pnpm install --frozen-lockfile`
2. `cd apps/kiosk-web && npx vitest run` → 277 passed; `-t "most recently reported"` → 3 passed.
3. Apply the counterfactual in Evidence 2 → SC-5 red; revert → green. (Proves the
   one-tile guard; not committed.)
4. `pnpm exec playwright test --list` before/after → identical.
5. Comment-stripped, literal-masked hash of the e2e file before/after → identical.
6. *Optional, phase 5:* a throwaway one-tile wall probe against the Aspire stack,
   recording `label_delay` sample count and values (T006). Not committed.

## Locked tech choices

No new dependency, no product code, no contract. Markdown + one Playwright file's
comments and two `console.info` literals.

## Latency budget

**N/A — no leg moves.** The label hold is not a §IV row (ADR-0129). Measured
label age, for the record the lane asked for: **two-tile, pre-204:** 35–45 ms
(spec 108 probe); **one-tile, pre-204:** 0 samples (hold never engaged);
**nine-tile, today, CI:** p50 59.5 ms, max 91.7 ms over 90 samples (run
`39065520458`), against the 200 ms cap. **One-tile, today:** not observed in a
browser; vitest SC-5 holds a 150 ms age for 150 ms.

## Out of scope

- Any change to `useWallAlignment.ts`, `LayoutGrid.tsx`, `useLabelDelay.ts` —
  the tile-count gate is FR-004 and correct; the hold no longer depends on it.
- A committed one-tile e2e fixture. Would be a new fixture for a case vitest
  already guards; if the owner wants live one-tile coverage, that is its own issue.
- #1714 (whole-path measurement, still open).
- `overlay_draw` exceeding 50 ms at max/p50 in the same CI log (p50 55.0 ms,
  max 102.2 ms) — ADR-0123's known breach, tracked elsewhere.

## Assumptions, marked

1. **"Already fixed" rests on vitest for the one-tile case**, not a live wall. The
   counterfactual shows the test detects the defect; it does not show a real
   `CameraViewer`'s lag sampler reaches `reportLag` on a 1×1 wall (it is the same
   code path as on nine tiles, where CI shows it does).
2. **The `[span] conditions` "one tile" meant the observed tile.** If it was meant
   as the wall's size it is simply wrong; either way the corrected literal is true.
3. **Spec 108's note is corrected by appending, not rewriting**, because it is a
   dated verification record other documents cite by line.
