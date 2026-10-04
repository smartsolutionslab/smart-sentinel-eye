# Tasks 299: The mix that lost its hue

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2695

## Phase-3 declarations

- **Lane:** supervised. Stop at every gate; the issue is deliberately not `agent:ready`.
- **Engineers:** `test-writer` (phase 4a — every C# architecture-test edit including
  `OklchColorResolver.Mix`, and the new Vitest file); `frontend-engineer` (phase 4b —
  `tokens.css`, `tailwindTheme.ts` only). **No backend-, infra- or other engineer**: nothing under
  `src/`, `AppHost`, CI or `deploy/` changes.
- **New ADR:** **no.** The two human decisions are written as in-place amendments of ADR-0146
  item 4 and ADR-0148 decision 2 (done in phase 1, T000). Everything else applies them.
- **Phase 4a colour: RED** for US1 and US2 (spec §6). Characterisation set observed green on
  `e892965b` and passing **unmodified** after. Declared assertion edits, and only these:
  `LabelSurface`'s expected text; `ColorMixValue`'s space; `A_triad_label_is_legible_on_its_tint`'s
  text role and theme list; deletion of `ContrastExclusions` + `Each_contrast_exclusion_still_fails`.
- **Latency:** N/A (spec §5).
- **Board:** #2695 is already on Project #13 (status Todo, verified 2026-10-04). No per-task
  issues (CLAUDE.md, Phase 3 since spec 028).
- **Shard filters:** no new test class, so no `shard-N.filter` entry.

Format: `[ID] [P?] [Story]`. `[P]` = owns files disjoint from every other `[P]` task in the same
group (ADR-0109).

## Phase 1–3 (architect — done here)

- [x] **T000** Amend ADR-0146 item 4 (per-theme triad text role) and ADR-0148 decision 2
  (`in oklab`), each with an inline "Amended" note at the decision and an `## Amendment
  (2026-10-04)` section; original text kept.
- [x] **T000a** spec.md, plan.md, tasks.md.

## Phase 4 prerequisites (orchestrator)

- [ ] **T001** Re-check spec number 299 across remote branches, worktrees and open PRs.
- [ ] **T002** Build management-web and kiosk-web on `e892965b`; keep both compiled CSS bundles as
  the phase-5 baseline (spec §7 step 2). Record the computed `color` of one Badge (each tone) and
  one `FaultNotice` in dark (plan R3 baseline).

## US1 (P1) — A derived colour keeps its hue

**Phase 4a — test-writer.** Run on `e892965b`; return verbatim output.

- [ ] **T010 [US1]** `OklchColorResolver.cs`: add `MixSpace` + `Mix(...)` (plan §4.1) — exact
  OKLab; CSS Color 4 OKLCH (shorter arc, written hue kept).
- [ ] **T011 [US1]** `InteractionStateTests.cs`: three model facts pinning the browser-observed
  values (plan §4.1) — expected **green** on first run; a red one stops the task (model wrong).
- [ ] **T012 [US1]** `InteractionStateTests.cs`: `ResolveOklch` parses `in (oklab|oklch)` and calls
  `Mix`; the higher-chroma-hue approximation and its comment are removed (plan §4.2). New fact
  `A_mix_toward_black_or_white_keeps_its_base_hue` (plan §4.3) — expected **red** naming hover,
  pressed (3 themes), high-contrast disabled/subtle, fault hover/pressed.
- [ ] **T013 [US1]** `StatusTintTests.cs`: `TryResolveSrgb`'s mix branch → same parse + `Mix`.
  (Characterisation: every existing `StatusTintTests` fact keeps its pre-change result.)
- [ ] **T014 [US1]** `DesignTokenLayerTests.cs`: `ColorMixValue` → `in oklab`; `LabelSurface` →
  `in oklab`; new `Every_colour_mix_in_the_token_file_mixes_in_oklab` — expected **red** (20
  declarations; fact 5 lists the 16 `--color-*`).
- [ ] **T015 [US1]** Run `dotnet test tests/Architecture.Tests` on `e892965b` with T010–T014; quote
  the red facts and the green characterisation set (spec §6) verbatim.

T010 → T011/T012/T013 (they call `Mix`). T014 is independent of T010 but shares nothing with
T012/T013 except the test run: **T014 [P]** with T011–T013.

**Phase 4b — frontend-engineer.** Given T015's output; may not edit tests.

- [ ] **T016 [US1]** `tokens.css`: every `color-mix(in oklch,` → `color-mix(in oklab,` (20
  declarations); update the comments at `:12-20`, `:70-74`, `:141-148` per plan §2.1.
- [ ] **T017 [US1]** Run Architecture.Tests: all US1 facts green, characterisation set unmodified
  and green. If light `--color-accent` on `--color-accent-subtle` drops below 4.5 : 1 (plan R1),
  **stop and report** — do not change the percentage without the declared plan-R1 change being
  acknowledged.
- [ ] **T018 [US1]** Commit 2 (plan §5): tests + resolver + tokens in one commit.

## US2 (P2) — A status label is legible in every theme

Depends on US1 (same `StatusTintTests.cs`; contrast facts use the corrected resolver).

**Phase 4a — test-writer.**

- [ ] **T020 [P] [US2]** `StatusTintTests.cs`: new `Each_triad_text_role_keeps_its_triad_hue`;
  `A_triad_label_is_legible_on_its_tint` → `-text` role, add `light`, drop exclusion check; new
  `A_triad_label_is_legible_on_every_ground`; delete `ContrastExclusions`, `IssueReference`,
  `Each_contrast_exclusion_still_fails`; update class remarks (plan §4.3).
- [ ] **T021 [P] [US2]** `apps/shared/src/ui/tokens/tailwindTheme.test.ts` (new): `textColor.accent`
  maps to the `-text` vars; `colors.accent.<role>` still maps to the signal vars.
- [ ] **T022 [US2]** Run Architecture.Tests and the shared Vitest suite; quote red output verbatim.

**Phase 4b — frontend-engineer.**

- [ ] **T023 [US2]** `tokens.css`: `--green-700`, `--amber-700`, `--red-700` (plan §2.2 values);
  `--color-accent-<role>-text` in `:root` (→ signal) and `light` (→ `-700`); rewrite the `light`
  comment at `:291-293`.
- [ ] **T024 [US2]** `tailwindTheme.ts`: `extend.textColor.accent.{active,warning,fault}` (plan §3).
- [ ] **T025 [US2]** Run Architecture.Tests, `pnpm -r test` for shared/management-web/kiosk-web,
  typecheck, lint, prettier. Every vitest asserting `text-accent-*` class names passes unmodified.
- [ ] **T026 [US2]** Commit 3 (plan §5).

T023 and T024 touch disjoint files but are one commit and one engineer; not worth splitting.

## Phase 5 — verify (spec §7)

- [ ] **T030** Compiled-CSS diff of both apps against T002's baseline; only the permitted
  differences (spec §7 step 2). Quote the diff summary.
- [ ] **T031** Chromium canvas read-back of US1's derived accents (dark + high-contrast); record
  rendered hues.
- [ ] **T032** `data-theme="light"`: computed colour + contrast of Badge (each tone) and
  `FaultNotice`; then `dark`: computed colours equal T002's baseline. Write every figure into the
  PR verification note.

## Phase 6–7

- [ ] **T040** `frontend-reviewer` over the diff (tokens, Tailwind, guards).
- [ ] **T041** PR to `develop` (`--base develop`), body quotes T015/T022 red output and phase-5
  figures, closes #2695 with a closing keyword; verify the issue state after merge.
