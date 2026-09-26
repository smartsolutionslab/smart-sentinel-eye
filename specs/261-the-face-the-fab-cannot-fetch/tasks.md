# Tasks 261: The face the fab cannot fetch

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2333 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing, spec §6). Vacuous-green assertions (G1, G2, G4, B3,
E1's first clause) are declared in advance and proven by counterfactual (plan §6.5). Their
green result is not 4a evidence.
**Engineers**: `test-writer` (4a); then `frontend-engineer` (4b). The one C# file is test
code only, so no backend engineer is needed. **Reviewers**: `frontend-reviewer` (+
`backend-reviewer` for `ExternalFontHostTests.cs`).
**Tracking**: feature-level issue #2333 (on Project #13, Todo). No per-task issues.

Format: `[ID] [P?] [Story] description`.

**Foundational / blocking**: T001 (devDependencies + lockfile) blocks T003/T004/T005, which
import from those packages. In 4b, T010 (the bytes) blocks T011 (`fonts.css`), and T011
blocks T012/T013. No Shared.Kernel, Shared.Contracts or AppHost work.
**Parallelism** (ADR-0109, disjoint files). In 4a, after T001: T002 ∥ T003 ∥ T004 ∥ T005 ∥ T006.
In 4b: T012 ∥ T013 ∥ T014 after T011, and T020 ∥ T021 after T015.

**Do not touch**: `Button.tsx`, `Input.tsx` (#2336); `OverlayEditor.tsx` and the other #2342
files; `overlayLabelStyle.ts` (its `fontWeight: 600` is an input to the preload decision, not
a change); any `prefers-reduced-motion` rule (#2334); `DesignTokenLayerTests.cs`,
`SharedUiTokenUsageTests.cs`, `tokens.build.test.ts` (spec 257's guards, which must stay
green unmodified).

## Phase 4a: red (test-writer; commit 2 of plan §8)

- [ ] **T001 [US1][US2][US3]** `apps/shared/package.json`: add the devDependencies
  `@capsizecss/core` `4.1.3`, `@capsizecss/metrics` `4.3.0`, `postcss` `8.5.26` and `vite`
  `8.2.2`, pinned exact (plan §5). Run `pnpm install`. Commit the lockfile with the tests.
  **Blocks T003–T005.**
- [ ] **T002 [P] [US1]** `tests/Architecture.Tests/ExternalFontHostTests.cs`: facts G1–G4 of
  plan §6.1. Use `RepositorySource.Root()`/`RelativePath`. Strip comments before
  matching, and report paths with forward slashes. The class doc-comment carries "bans a
  category, never a value" and "what this scan cannot see" (TS-built stylesheets and
  `FontFace`s, dependency CSS, assembled `url()`s → B3 and E1 are the authorities). G3
  carries the "scan is broken, not the code" self-check.
- [ ] **T003 [P] [US1][US2]** `apps/shared/src/test/woff2.ts` (WOFF2 header, table directory,
  brotli stream; `head`, `hhea`, `hmtx`, cmap formats 4 and 12) and
  `apps/shared/src/ui/fonts/fonts.test.ts`: facts S1–S5 of plan §6.2. The SHA-256 table and
  the Arial digit constant `1139/2048` are **literals cited in comments** to plan §1 / §3.3.
  They are not read back from the subject. S3 uses the TypeScript compiler API and
  self-checks that at least one character was found.
- [ ] **T004 [P] [US1][US2][US3]** `apps/kiosk-web/src/styles/fonts.build.test.ts`: facts
  B1–B5 of plan §6.3 (`@vitest-environment node`, real `vite build` into `mkdtemp`, a 180 s
  `beforeAll`, cleanup in `afterAll`).
- [ ] **T005 [P] [US1][US2][US3]** `apps/management-web/src/styles/fonts.build.test.ts`: the
  same as T004, for management-web. It differs only in its header comment and `describe`
  title.
- [ ] **T006 [P] [US1][US2][US3]** `e2e/self-hosted-fonts.spec.ts`: facts E1–E4 of plan §6.4,
  in the `chromium` project, on the unauthenticated sign-in screen, with listeners attached
  before `goto`. Waits are by condition (ADR-0150). E4 asserts the fallback face loaded
  **before** measuring.
- [ ] **T007** Run and capture **verbatim**:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~ExternalFontHost"`,
  `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/fonts/fonts.test.ts`,
  `pnpm --filter @smart-sentinel-eye/kiosk-web exec vitest run src/styles/fonts.build.test.ts`,
  the same for management-web, and `pnpm test:e2e e2e/self-hosted-fonts.spec.ts --project chromium`
  against a running stack (one stack per machine, so coordinate with the orchestrator).
  **Required**:
  - G3 red ("no `@font-face` url() found"); G1, G2, G4 green (vacuous).
  - S1–S5 red.
  - B1, B2, B4, B5 red in both apps; B3 green (vacuous).
  - E1 red on its second clause, E2–E4 red.
  - `DesignTokenLayerTests`, `SharedUiTokenUsageTests` and both `tokens.build.test.ts` still
    green (unchanged).

  If any assertion expected red arrives green, **stop and report**. Do not adjust it.
- [ ] **T008** Counterfactuals 1–3 of plan §6.5, in a scratch copy or with an immediate revert.
  Quote each failure, then revert. `git status` must be clean of them afterwards. They go
  in the PR body with T007's output. Commit T001–T006 as
  `test(fonts): pin the self-hosted font pipeline red-first`.

Depends: T001 → T002–T006 → T007 → T008.

## Phase 4b: implement (frontend-engineer; must not edit T002–T006)

### US1 + US2 (commit 3 of plan §8, one commit)

- [ ] **T010 [US1]** Fetch `@ibm/plex-sans@1.1.0` and `@ibm/plex-mono@2.5.0` with `npm pack`
  **into a scratch directory**, and verify each tarball's sha512 against plan §1 before
  extracting. Copy the twelve `fonts/split/woff2/` files of plan §1 **unmodified** to
  `apps/shared/src/ui/fonts/`, and copy `package/LICENSE.txt` to
  `apps/shared/src/ui/fonts/OFL.txt`. Add `*.woff2 binary` to `.gitattributes`'s
  "Explicit binaries" block. No subsetting tool is run. **Blocks T011.**
- [ ] **T011 [US1][US2]** Create `apps/shared/src/ui/fonts/fonts.css` per plan §2.2 and §3,
  with the header comment of plan §2.2.
  - Twelve Plex faces: `font-display: swap`, **no `local()`**, and `unicode-range` copied
    verbatim from IBM's `IBMPlex*-<Weight>.css` for that exact file.
  - The four general fallback faces with the §3.2 values and `src` lists.
  - The three digit faces of §3.3, each **declared after** the general face of its weight.

  Add `"./ui/fonts/*": "./src/ui/fonts/*"` to `apps/shared/package.json` `exports`.
  **Blocks T012–T014.**
- [ ] **T012 [P] [US2]** `apps/shared/src/ui/tokens/tokens.css`: prepend
  `'IBM Plex Sans', 'IBM Plex Sans Fallback'` to `--font-sans` and
  `'IBM Plex Mono', 'IBM Plex Mono Fallback'` to `--font-mono`, keeping the tails verbatim.
  Replace the "#2333 prepends…" comment per plan §2.3.
- [ ] **T013 [P] [US1]** `apps/kiosk-web/src/main.tsx`: add
  `import '@smart-sentinel-eye/shared/ui/fonts/fonts.css';` before `./styles/index.css`.
- [ ] **T014 [P] [US1]** `apps/management-web/src/main.tsx`: the same as T013.
- [ ] **T015** Run T007's commands. S1–S5, G1–G4, B1–B3, B5 and E1, E2, E4 must be green;
  B4 and E3 are still red (the preload is not wired yet). Run the full `pnpm test`,
  `pnpm typecheck`, `pnpm lint`, `pnpm format:check` and
  `dotnet test tests/Architecture.Tests`. **If E4 is red on width, stop and report (plan
  R4). Do not widen the tolerance.** Commit as
  `feat(fonts): self-host IBM Plex with metric-matched fallbacks`.

### US3 (commit 4 of plan §8)

- [ ] **T016 [US3]** Create `apps/shared/src/ui/fonts/fontPreload.ts` per plan §4.1: order
  `pre`, a `/@fs/` absolute path in serve mode, a root-relative path in build mode,
  `crossorigin`, and a throw from `configResolved` when a named file is missing. **Blocks
  T020, T021.**
- [ ] **T020 [P] [US3]** `apps/kiosk-web/vite.config.ts`: add
  `fontPreload(['IBMPlexSans-Regular-Latin1.woff2', 'IBMPlexSans-SemiBold-Latin1.woff2'])`,
  with a one-line *why* comment (the screen headings and the overlay labels at 600).
- [ ] **T021 [P] [US3]** `apps/management-web/vite.config.ts`: the same list, with its own
  *why* comment (the sign-in `<h1>` + body).
- [ ] **T022** Run T007's commands again. Everything is green, B4 and E3 included. Run the
  full checks of T015. Commit as
  `feat(fonts): preload the first screen's faces at the URL the stylesheet uses`.

Depends: T010 → T011 → {T012, T013, T014} → T015 → T016 → {T020, T021} → T022.

## Phase 5 (verify) and after

- [ ] **T030** Spec §7 steps 1–7 on the live stack. Record the observed preload URLs (dev
  and build) and the fonts-blocked vs. fonts-loaded screenshot pair in the verification
  note.
- [ ] **T031** Plan §6.5 counterfactual 4 (S4 value nudge, S1 byte flip). Quote both in the
  PR, then revert.
- [ ] **T032** Immediately before `gh pr create --base develop`, re-list every branch and
  worktree for a `specs/261-*` collision (spec header, plan R6).
