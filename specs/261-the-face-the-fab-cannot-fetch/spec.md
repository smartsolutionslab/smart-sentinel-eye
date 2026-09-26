# Spec 261 — The face the fab cannot fetch

**Issue:** [#2333](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2333)
— *A self-hosted font pipeline, because the fab cannot reach a CDN*. Labels
`enhancement`, `agent:ready`. Phase 01 of the frontend redesign programme. On Project
#13 as **Todo**, verified 2026-09-26 with `gh issue view 2333 --json projectItems`, so
no `item-add` is needed. Gates: #2330 is closed (ADR-0147), and #2332's token PR
(#2604) is merged. This branch is rebased on it (`b2f09bf7`).

**Spec number.** 261, not 258. On 2026-09-26 every local and remote branch and every
worktree under `D:/Github/` was listed. Develop's highest spec is 257. **258 is claimed
three times, in untracked directories**: `sse-2486` (`258-the-bundle-the-hook-still-honours`),
`sse-2607` (`258-the-tile-that-claims-a-rectangle`) and `sse-2608`
(`258-a-wall-that-changes-its-scene`). Two of those three must renumber. They will
reach for 259 and 260, so this spec leaves both free. **Re-check before opening the
PR**, as spec 257's header warns.

**ADRs referenced:**

- **ADR-0147** (IBM Plex Sans, self-hosted): the decision this spec implements. Its
  §"What implementation must include" items 1–5 map one-to-one onto §2 below. Two
  things it already decided are not reopened here: **static faces, not variable**, and
  **SIL OFL 1.1**.
- **ADR-0148** (two-layer tokens) and spec 257: the `--font-sans`/`--font-mono`
  tokens this spec extends. Spec 257 names this issue as the one that prepends the Plex
  families.
- **ADR-0078** (Tailwind + CSS custom properties): the mechanism. It is unchanged.
- **ADR-0074** (two Vite apps + `apps/shared`): why the font bytes live once, in
  `apps/shared`.
- **Constitution §I** (no outbound internet dependency): the reason this is its own
  issue.
- **ADR-0139 / ADR-0144** (red first; phase-4a colours). §6 picks **red**.
- **ADR-0036** (smallest change), **ADR-0037** (phases and gates), **ADR-0109**
  (`[P]`), **ADR-0052 / 0053** (xUnit + Shouldly; sentence-style names), **ADR-0108**
  (browser e2e against the live stack).

**No new ADR.** See §4. One question sits next to ADR-0147 without being decided by
it: OFL §3's Reserved Font Name. This spec **avoids** that question rather than
answering it. §1 finding 3 explains how.

**Latency budget (§IV): no leg is spent.** See §5.

---

## 1. The issue's premise, re-checked on this tree (`b2f09bf7`, 2026-09-26)

| Claim in #2333 / ADR-0147 | On this tree |
|---|---|
| No typeface is declared | **True.** No `@font-face` exists under `apps/**`. `--font-sans`/`--font-mono` in `apps/shared/src/ui/tokens/tokens.css` hold Tailwind 4.3.3's system stacks verbatim. The comment on them names this issue. |
| "The UI is English today; check whether any fab-facing string is not" | **Checked with the TypeScript compiler API, not grep.** Every string literal, template literal and JSX text in non-test files under `apps/{kiosk-web,management-web,shared}/src` was scanned. The non-ASCII characters the product renders are `·` U+00B7, `×` U+00D7, `–` U+2013, `—` U+2014, `“` U+201C, `”` U+201D, `…` U+2026, `↑` U+2191, `→` U+2192, `↓` U+2193, `↕` U+2195, and one `U+200B` (zero-width space, `OverlayEditor.tsx`). **None of them is a letter from a non-Latin script.** The UI is English. |
| Variable-axis wiring "if #2330 picks a variable font" | **Not applicable.** ADR-0147 §Consequences: "Weights ship as static faces unless measured otherwise … Start static". Nothing is measured otherwise, so issue item 6 is out of scope (§3). |
| `ContainerImagePinTests` is the pattern for the guard | **True** (`tests/Architecture.Tests/ContainerImagePinTests.cs`). §2 US1 follows its shape: a category ban, a self-check that fails when the scan matches nothing, and a section on what the scan cannot see. |

Five findings that neither the issue nor ADR-0147 records. Each was **measured** on
2026-09-26, and each changes the design.

1. **Tailwind's `@import` inlining does not rebase `url()`, so a font declared that
   way disappears from the production build.** Reproduced with the installed Vite
   8.2.2 and `@tailwindcss/postcss` 4.3.3. A shared stylesheet holding
   `url('./x.woff2')` was pulled in through `@import` from an `index.css` that
   Tailwind processes. `vite build` reported `./x.woff2 … didn't resolve at build
   time, it will remain unchanged` and shipped `src:url(./x.woff2)` beside
   `/assets/`, which is a 404. **The dev server rewrote the same `url()` correctly**
   (`/@fs/<absolute path>`). So the obvious wiring works on every developer machine
   and ships a missing font, and the fallback hides it: the exact failure the issue
   describes, reached by a different route. When the same stylesheet is imported **from
   JavaScript** (`import '…/fonts.css'` in `main.tsx`), both modes resolve it:
   `/@fs/…` in dev, and `/assets/x-<hash>.woff2` in build. → plan §2.
2. **A `<link rel="preload">` written into `index.html` works in build and breaks in
   dev.** Also reproduced on Vite 8.2.2. In build, Vite rewrites
   `href="../shared/…/x.woff2"` to the same hashed URL the CSS uses, and emits one
   asset. In dev the href passes through unchanged, and the SPA fallback answers it
   with **`200 text/html`**. So the preload fetches a page, the font is fetched a
   second time from `/@fs/…`, and nothing reports it. **This matters because the fab
   runs the dev server**: `AppHost` starts all three frontends with
   `AddNpmApp(…, "dev")`, and no production deployment exists (CLAUDE.md, "What
   lives where"). A small `transformIndexHtml` plugin (order `pre`) that emits the
   `/@fs/` path in serve mode and the root-relative path in build mode produced a
   **byte-identical href to the CSS `url()` in both modes**. → plan §4.
3. **IBM publishes the subsets itself.** `@ibm/plex-sans@1.1.0` and
   `@ibm/plex-mono@2.5.0` are IBM's own npm releases of the `IBM/plex` repo. Each ships
   `fonts/split/woff2/` per weight, split into `Latin1`, `Latin2`, `Latin3`, `Pi`,
   `Cyrillic` and `Greek`, with IBM's own `unicode-range` for each. `Latin1` +
   `Pi` cover every character in the table row above. `Latin2` covers Latin
   Extended-A, which operator-entered names such as `Łódź` or `Plzeň` need. **Shipping
   IBM's files unmodified means this repository never creates a Modified Version under
   OFL §3.** So the Reserved Font Name "Plex" restriction, which a subset *we* cut would
   engage, never arises. It also removes the subsetting tool from the pipeline
   entirely. → §4, plan §1.
4. **Plex Sans has no `tnum` feature, and does not need one.** Its GSUB feature list
   is `aalt ccmp dnom frac liga lnum locl numr onum ordn salt sinf ss01–ss06 subs sups
   zero`. The **default** digits `0`–`9` all advance 600 units, in Regular and in
   SemiBold. So Plex's figures are tabular by default, which is what ADR-0147's "true
   tabular figures" means in practice. The `font-variant-numeric: tabular-nums` that
   spec 257 applies is a no-op on Plex and still earns its place on the fallback
   faces. Recorded so nobody "fixes" the absent feature.
5. **The first screen of both apps, and the wall's overlay labels, are SemiBold.**
   Every kiosk screen's `<h1>` is `font-semibold`. `overlayLabelStyle.ts` sets
   `fontWeight: 600` on every label drawn over video. Both apps' sign-in and error
   screens pair a SemiBold `<h1>` with Regular body text. Regular alone would therefore
   be the wrong face to preload. → US3.

## 2. User stories

### US1 (P1): The product renders IBM Plex from its own origin, and CI fails if anything reaches outside it

Both apps render UI text in IBM Plex Sans, and identifiers, predicates and paths in
IBM Plex Mono. The bytes come from the app's own origin: IBM's unmodified woff2
subsets, committed once under `apps/shared/src/ui/fonts/` and bundled by Vite into
each app. No stylesheet, `index.html` or built asset references another host. A
change that adds one fails the build, in the way `ContainerImagePinTests` fails the
build for a floating image tag.

**Why P1:** this is the issue. Everything else refines it. It can be observed on its
own. Open either app, and every font request goes to the app's own origin while the
computed family is Plex.

```gherkin
Scenario: happy path — Plex renders, fetched from the app's origin
  Given the running stack and an operator signed in to management-web
  When the shell has rendered and document.fonts.ready has resolved
  Then the body's computed font-family begins with "IBM Plex Sans"
  And document.fonts.check('400 16px "IBM Plex Sans"') is true
  And at least one IBMPlexSans-Regular-Latin1 woff2 was fetched with status 200
  And every font request the page made went to the page's own origin

Scenario: happy path — the bytes ship in the build
  Given `vite build` of either app
  Then every @font-face url() in the built CSS is a root-relative path under /assets/
       naming a file that exists in the output directory
  And each of the twelve committed woff2 files appears byte-identical in the output

Scenario: the committed bytes are IBM's, unmodified
  Given apps/shared/src/ui/fonts/
  Then each of the twelve woff2 files has the SHA-256 of the same-named file in
       @ibm/plex-sans@1.1.0 or @ibm/plex-mono@2.5.0 (plan §1 lists them)
  And OFL.txt is IBM's LICENSE.txt from those packages (SHA-256 after CRLF→LF)

Scenario: every range a face declares, its file can draw
  Given each IBM Plex @font-face in fonts.css
  Then every code point in its unicode-range is present in the cmap of the file its
       url() names

Scenario: every character the product renders is covered
  Given every non-ASCII character in a string literal, template literal or JSX text
        of a non-test file under apps/{kiosk-web,management-web,shared}/src
  Then each is inside the unicode-range of some IBM Plex Sans weight-400 face

Scenario: bad request — a stylesheet reaches for a CDN
  Given any .css file under apps/** (node_modules and dist excluded), comments stripped
  When it contains url(…) or @import whose target begins http:, https: or //
  Then ExternalFontHostTests fails, naming the file, the line and the URL

Scenario: bad request — index.html reaches for a CDN
  Given apps/*/index.html
  When a <link> has an href beginning http:, https: or //
  Then ExternalFontHostTests fails, naming the file and the href

Scenario: bad request — a self-hosted face defers to an installed copy
  Given an @font-face under apps/** with a url() source
  When the same rule also has a local() source (IBM's own CSS does this)
  Then ExternalFontHostTests fails: a machine with a different Plex installed would
       render its own copy, which is the fleet disagreement ADR-0147 exists to end

Scenario: conflict — a declared font file is missing
  Given an @font-face url() under apps/**
  When the file it names does not exist relative to the stylesheet
  Then ExternalFontHostTests fails, naming the rule and the missing path

Scenario: the scan is live
  When ExternalFontHostTests finds no @font-face url() source anywhere under apps/**
  Then it fails with "the scan is broken, not the code", as ContainerImagePinTests does
```

*Auth: N/A. No endpoint, scope or token is involved. Fonts are served by the same dev
server or static origin as the app, before and after sign-in alike.*

### US2 (P1): When the fallback paints, nothing moves

Each Plex family is followed in its stack by a **metric-matched local fallback**:
`IBM Plex Sans Fallback` (Arial, or its metric clone Liberation Sans / Arimo on Linux
walls) and `IBM Plex Mono Fallback` (Courier New, or Liberation Mono / Cousine). Their
`size-adjust`, `ascent-override`, `descent-override` and `line-gap-override` are the
values `@capsizecss/core` derives from `@capsizecss/metrics`, one fallback face per
Plex weight shipped. Each Sans weight also gets a **digits-only fallback face**
(`unicode-range: U+0030-0039`). Capsize's width match is tuned to English letter
frequency, and it leaves `12:34:56.789` 5.6–8.6% narrower than Plex, whose digits are
wider than Arial's (measured, plan §3.3). The fallback paints before Plex arrives, or
instead of it if a file ever goes missing. Either way, the swap moves no line box, and
no column of timestamps.

**Why P1:** ADR-0147 calls this "the step usually skipped and the one that matters
most on a wall nobody is watching". Without it, Plex's line box (1.300 em from hhea
1025/−275/0) and Arial's (1.150 em) differ by 13%, so every `line-height: normal`
element would jump on swap.

```gherkin
Scenario: happy path — the declared overrides are the derived ones
  Given fonts.css's fallback faces
  Then for each (Plex face, fallback) pair —
         Sans 400 / Arial regular, Sans 500 / Arial regular,
         Sans 600 / Arial 700, Mono 400 / Courier New regular —
       the declared size-adjust, ascent-override, descent-override and
       line-gap-override equal @capsizecss/core createFontStack's output to 4 decimals

Scenario: happy path — digits keep Plex's width in the fallback
  Given each IBM Plex Sans weight (400, 500, 600)
  Then all ten digits in its shipped Latin1 file share one advance
  And fonts.css declares, after that weight's general fallback face, a fallback face
      with unicode-range U+0030-0039 whose size-adjust is that advance over Arial's
      digit advance (1139/2048) and whose ascent/descent/line-gap overrides keep the
      1.300 em line box

Scenario: the metrics describe these bytes
  Given each committed Plex woff2
  Then its head.unitsPerEm and hhea ascender/descender/lineGap equal
       @capsizecss/metrics' entry for that family and weight

Scenario: happy path — the swap does not move the layout (browser, measured)
  Given the running stack, management-web signed in, fonts ready
  And the fallback face is loaded (status "loaded"; fails loudly if the machine has
      neither Arial nor Liberation Sans)
  When a prose sample and a digit sample each render at 16px, line-height normal,
       once in each Plex face and once in its fallback
  Then the heights differ by at most 1px
  And the baseline offsets differ by at most 1px
  And the widths differ by at most 3%

Scenario: the stacks are wired through the tokens, not per component
  Given the built CSS of either app
  Then --font-sans begins 'IBM Plex Sans', 'IBM Plex Sans Fallback', followed by
       Tailwind 4.3.3's system stack
  And --font-mono begins 'IBM Plex Mono', 'IBM Plex Mono Fallback', followed by
       Tailwind 4.3.3's monospace stack
```

*Bad request / conflict / auth: N/A beyond US1's. A wrong override value is caught by
the first scenario.*

**Stated gap, deliberately.** The browser scenario measures on the machine running
the e2e job: Liberation Sans on the Ubuntu CI runner. Liberation Sans was drawn to
Arial's advance widths and vertical metrics, so the result transfers to a Windows
kiosk. It is **not** a measurement on that kiosk. Nor is it a cumulative-layout-shift
figure for a whole page: no CLS tool is wired into this repository. The claim is
narrower and checkable: *the fallback's line box, baseline and average advance match
Plex's.* A page built from boxes that match does not shift. Phase 5 adds one manual
look at a kiosk (§7).

### US3 (P2): The faces the first screen needs arrive before it paints, and no others

Each app preloads exactly the faces its first screen renders: **IBM Plex Sans 400 and
600, `Latin1` subset**. Nothing else is preloaded: no Medium, no `Latin2`/`Pi`, no
Mono. The preload href is the **same URL** the `@font-face` rule fetches, in the dev
server that the fab actually runs and in `vite build` alike. So each face is fetched
once.

**Why P2:** with US2 in place, a late face costs a glyph-shape change and no movement.
Preload shortens that window. It does not make it safe. **Per app:**

| App | First screen | Preload | Not preloaded, and why |
|---|---|---|---|
| kiosk-web (incl. `kiosk-wall`) | SemiBold `<h1>` on every screen (picker, reconnecting, recovery, expired); **overlay labels at `fontWeight: 600`** over video; Regular body | Sans 400 Latin1, Sans 600 Latin1 | Sans 500: the cell/picker `<h2>` sits under the heading. Mono: never used by kiosk-web (`font-mono` appears only in management-web). |
| management-web | SemiBold `<h1>` + Regular button/body on the sign-in screen; the same pairing on the shell | Sans 400 Latin1, Sans 600 Latin1 | Sans 500: the active nav pill, which is small and appears after sign-in. Mono: predicates, identifiers and values render only after a data fetch, below the fold. |

```gherkin
Scenario: happy path — the build preloads exactly the first screen's faces
  Given `vite build` of either app
  Then dist/index.html has exactly two <link rel="preload" as="font">
  And each has type="font/woff2" and a crossorigin attribute
  And their hrefs equal the url() of the IBM Plex Sans 400 and 600 faces whose
      unicode-range contains U+0041

Scenario: happy path — the dev server preloads the URL the stylesheet fetches
  Given the running stack (vite dev), management-web signed in, on a cold load
  Then each preload link's href resolves to a font the page loaded
  And each preloaded URL was requested exactly once

Scenario: bad request — a preload the stylesheet never uses
  When a preload href names a file no @font-face url() in the built CSS names
  Then the build test fails, naming the href
```

## 3. Scope

### In this PR

- Twelve IBM woff2 files, unmodified, plus IBM's OFL licence, under
  `apps/shared/src/ui/fonts/`. Sans 400/500/600 × `Latin1`/`Latin2`/`Pi`, and Mono 400
  × `Latin1`/`Latin2`/`Pi`.
- `apps/shared/src/ui/fonts/fonts.css`: twelve Plex `@font-face` rules
  (`font-display: swap`, no `local()`, IBM's own `unicode-range` per file) and seven
  fallback faces: four general faces (Sans 400/500/600, Mono 400) and three digit faces
  (Sans 400/500/600).
- `--font-sans` / `--font-mono` in `tokens.css` gain the two family names in front of
  the unchanged system stacks.
- `fonts.css` imported from each app's `main.tsx` (finding 1), and exported from
  `@smart-sentinel-eye/shared`.
- `fontPreload`, a Vite plugin in `apps/shared` (finding 2), wired into both
  `vite.config.ts` files with each app's preload list.
- The tests in §6 / plan §6.
- `*.woff2 binary` in `.gitattributes`.

### Deferred, and to where

| Item | Where | Why not here |
|---|---|---|
| Variable-axis wiring (issue item 6) | Nowhere now | ADR-0147 decided static. Revisit only "if the type scale turns out to want optical sizing", which would take a measurement and an amendment to ADR-0147. |
| Cyrillic, Greek, Vietnamese (`Latin3`) subsets | Whichever spec ships a non-Latin fab | Nothing renders them today (§1). A character outside every shipped range falls back **per glyph** to the next family in the stack. It renders, it does not show tofu, and it moves no layout that Plex did not already set. The coverage test (US1) fails the moment the product itself starts rendering one. |
| A subset cut by this repository (pyftsubset or similar) | A spec that needs one, **with a licence read** | Creates an OFL Modified Version, which engages the Reserved Font Name. ADR-0147's own rejection of licensed faces sets the bar: "a licence read by someone willing to sign it". |
| Whole-page CLS measurement on real kiosk hardware | Phase 5 manual look (§7), then nowhere automated | No CLS tooling exists here. US2's box-level claim is the automatable part. |
| Inter-display typography sync, font hinting review on Windows kiosks | Not planned | Out of the issue's scope. |

## 4. Why no new ADR

Every structural question is answered by ADR-0147 (face, licence, static weights,
self-host, subset, `swap`, fallback overrides, preload above the fold, the guard),
ADR-0148 (the tokens carry the stack) and ADR-0074 (shared package). What this spec
chooses inside them is implementation detail, written down in plan.md so it is not
silent: the file source, the subset split, the fallback pairs, the derivation tool,
the preload list, the import route and the plugin.

**The one adjacent question is avoided, not decided.** OFL §3 forbids a Modified
Version from using a Reserved Font Name without the holder's permission, and "Plex"
is reserved (IBM's licence, line 1). Whether a subset served under the CSS family name
"IBM Plex Sans" is such a use is a licence reading. ADR-0147 did not make it, and an
architect should not make it silently. Finding 3 makes the question moot for this
delivery, because every byte shipped is IBM's own subset. If a later need requires a
home-cut subset, **that** spec needs the licence read, and probably an ADR-0147
amendment. §3 records it there.

## 5. Latency budget (§IV)

**No leg is spent.** The event → overlay path is unchanged. Overlay labels are DOM
text (no canvas `fillText` exists in `apps/**`), so a label drawn before Plex arrives
re-renders in Plex, once, at boot. With US2, the re-render moves no box.
`font-display: swap` never blocks a paint, so **composite + render (≤ 50 ms) is not
touched per event**. The one-time boot cost is two preloaded woff2 files, 20,984 +
22,260 bytes, fetched over the fab LAN from the kiosk's own origin. §VII's
dashboard rule: N/A, because no leg changes.

## 6. Phase-4a colour: **red**

Everything here is new behaviour: a new face, new rules, a new plugin, new guards.
Nothing is behaviour-preserving, so nothing is characterisation. The rule is ADR-0144:
every new-behaviour assertion is observed **red on develop**. **Declared vacuous-green
in advance, and not 4a evidence:** the three "nothing external" bans. On develop they
pass because nothing is declared at all. Each gets a **planted counterfactual** in 4a
(plan §6.5), quoted in the PR:

| Assertion | Develop | 4a evidence |
|---|---|---|
| ExternalFontHost: no absolute URL in apps CSS | vacuous green | counterfactual: `@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans');` in a scratch copy of `apps/kiosk-web/src/styles/index.css` |
| ExternalFontHost: no absolute `<link href>` in index.html | vacuous green | counterfactual: `<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>` |
| ExternalFontHost: no face mixes `url()` and `local()` | vacuous green | counterfactual: IBM's own rule, `src: local("IBM Plex Sans"), url("x.woff2")` |
| ExternalFontHost: every `url()` file exists; scan is live | **red** (no `@font-face`) | observed |
| Build test: no absolute URL in built CSS/HTML | vacuous green | counterfactual: the same `@import` planted, then the build test run |
| e2e: every font request same-origin | vacuous green (no font requested) | paired with the red "a Plex woff2 was fetched" in the same test |
| Everything else in plan §6 | **red on content** | observed |

The fonts.test.ts facts are red because `fonts.css` and the woff2 files are absent. On
its own that would be a weak red, a red on a missing file. For the two value-bearing
facts (overrides and metrics), 4a therefore also plants a counterfactual **after** 4b
lands: change one override by 0.01% and confirm red. The PR quotes it.

## 7. Independent end-to-end test procedure

1. `aspire run` (one stack per machine). Open management-web (`http://localhost:5173`)
   in Chromium with DevTools → Network → filter *Font*, "Disable cache" on.
2. Before signing in: two font requests, `IBMPlexSans-Regular-Latin1` and
   `IBMPlexSans-SemiBold-Latin1`. Each is `200`, from `localhost:5173/@fs/…`,
   initiator `preload`, and **appears once**. No request to any other host. The
   console has no "preloaded but not used" warning.
3. Elements → `<h1>` → Computed → *Rendered Fonts*: "IBM Plex Sans SemiBold — Network
   resource". Do the same on body text for Regular.
4. Sign in, open Rules. A predicate cell renders "IBM Plex Mono — Network resource",
   and `IBMPlexMono-Regular-Latin1` is fetched only now.
5. Network → throttle to *Offline* after first load, block `*.woff2`, hard reload.
   The page renders in the fallback. **Nothing jumps** when blocking is lifted and the
   page reloads normally. Compare a screenshot of each: headings, buttons and table
   rows sit on the same lines.
6. Open the kiosk and the wall (`kiosk-web`, `kiosk-wall`) and repeat 2–3. The wall's
   overlay labels render "IBM Plex Sans SemiBold".
7. `pnpm --filter @smart-sentinel-eye/kiosk-web build` and open `dist/index.html`'s
   preload hrefs: both name `/assets/IBMPlexSans-…-Latin1-<hash>.woff2`, and the
   built CSS names the same.

## 8. Success criteria

- SC1: every font request either app makes goes to its own origin (e2e, US1).
- SC2: the build output of each app contains all twelve Plex files, byte-identical to
  IBM's release, and every `@font-face url()` resolves inside it (build test, US1).
- SC3: an absolute URL in any apps CSS or `index.html` `<link>` fails CI
  (`ExternalFontHostTests`; counterfactual quoted).
- SC4: fallback ↔ Plex: height ±1px, baseline ±1px, width ±3% at 16px (e2e, US2).
- SC5: each app preloads exactly Sans 400 + 600 `Latin1`, each fetched once, in dev and
  in build (e2e + build test, US3).
- SC6: `pnpm test`, `pnpm typecheck`, `pnpm lint`, `pnpm format:check` and
  `dotnet test tests/Architecture.Tests` are all green.
