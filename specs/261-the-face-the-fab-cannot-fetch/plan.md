# Plan 261: The face the fab cannot fetch

**Spec**: [spec.md](spec.md) · **Issue**: #2333 · **Phase**: 2 (Plan)

## Constitution / ADR check

| Rule | How this plan satisfies it |
|---|---|
| §I, no outbound dependency | Every byte comes from the app's own origin. Three guards cover it: source (`ExternalFontHostTests`), built output (`fonts.build.test.ts`) and runtime (`e2e/self-hosted-fonts.spec.ts`). The pipeline itself is online **once**, at authoring time (`npm pack` of IBM's packages). CI and the fab never fetch a font. |
| ADR-0147 items 1–5 | §1 subset, §2 `@font-face` + `swap`, §3 fallback overrides, §4 preload, §6.1 the guard. |
| ADR-0147 "static faces" | Twelve static woff2 files. No variable font, no axis wiring. |
| ADR-0148 / spec 257 | Only `--font-sans`/`--font-mono` change (§2.3). No component changes: `font-sans`/`font-mono` already resolve through the token. |
| ADR-0074 | One copy of the bytes, in `apps/shared`. Both apps bundle it. |
| ADR-0109 | Disjoint files per task; see tasks.md. |
| ADR-0036 | No home-grown subsetting, no font-loading JS, no CLS framework. One ~30-line Vite plugin, needed because dev and build resolve a shared asset differently (spec §1 finding 2). |
| §II / C# house rules | Only test code in C#: one Architecture.Tests class. Collection expressions, no `_` fields, sentence-style names, Shouldly. |

## Bounded context and layers

No backend context is touched. Frontend only:

- `apps/shared` (package `@smart-sentinel-eye/shared`): new `src/ui/fonts/`, which holds
  the bytes, `fonts.css`, `fontPreload.ts` and the licence. It also gains a new `exports`
  entry and three pinned devDependencies.
- `apps/shared/src/ui/tokens/tokens.css`: two token values and one comment.
- `apps/kiosk-web`, `apps/management-web`: `main.tsx` gains one import line, and
  `vite.config.ts` gains one plugin.
- `tests/Architecture.Tests`: one new guard.
- `e2e/`: one new spec in the existing `chromium` project.

No domain model, no messaging, no Shared.Contracts, no AppHost resource (the dev server
already serves `/@fs/` and `/assets/`).

## 1. The bytes: IBM's own subsets, unmodified

**Source.** IBM's npm releases of `IBM/plex`, fetched once by the 4b engineer with
`npm pack` into a scratch directory, never into the repo:

| Package | Registry integrity (verify before extracting) |
|---|---|
| `@ibm/plex-sans@1.1.0` | `sha512-WPgvO6Yfj2w5YbhyAr1tv95RUz4LRJlqN+CmYvBglabXteufP1D1E9BABMde+ZIKdRbFJDoKF5eQzfhpnbgZcQ==` |
| `@ibm/plex-mono@2.5.0` | `sha512-STBJIPxPomOYPmBMO7z5TKPJUotAF9u3gAUumTqVgwgrAO+K4FRNh0MlhsoJjKhJKsMbBJR10/bk4inkj/wc1w==` |

**Why these files, and no subsetting tool.** Spec §1 finding 3. `fonts/split/woff2/` holds
IBM's own subsets, with IBM's own `unicode-range` per file in the sibling
`IBMPlex*-<Weight>.css`. Shipping them byte-for-byte means we never create an OFL
Modified Version (spec §4). Every range was checked against its file's `cmap` on
2026-09-26 with a scratch WOFF2 reader: **all twelve files cover every code point
their range declares.** All twelve also report `unitsPerEm` 1000 and hhea 1025 / −275 / 0,
which equals `@capsizecss/metrics`.

**Copied verbatim into `apps/shared/src/ui/fonts/`**, keeping IBM's file names so
provenance is obvious. SHA-256 values were measured 2026-09-26. Fact S1 pins them.

| File | Bytes | SHA-256 |
|---|---|---|
| `IBMPlexSans-Regular-Latin1.woff2` | 20,984 | `b5ad7bd39f996144915f0ad9849a90183b27d8c28ad97ed98af5b1bebc51f6b1` |
| `IBMPlexSans-Regular-Latin2.woff2` | 15,212 | `fcfa82e0d46ed01a8df38610b451860ca7e7518770c52fdd2a1d136247c40f28` |
| `IBMPlexSans-Regular-Pi.woff2` | 7,500 | `1487059829a180f975627e473acc81ff22c2c0faf1da09b314c27eeb41b7f2e4` |
| `IBMPlexSans-Medium-Latin1.woff2` | 21,960 | `b5610af04d0d4b5a14a621d96d974b993e945a065db1a8861918f69ef9321934` |
| `IBMPlexSans-Medium-Latin2.woff2` | 15,372 | `46fbe4f398e7ff3cc7773a1156b1efef0f81e4d034f682369c221de587f443ca` |
| `IBMPlexSans-Medium-Pi.woff2` | 7,768 | `bf05f10c977353cfb5a5c11e8973adf77c2b93a4798da3aa0dd8ba5088e12515` |
| `IBMPlexSans-SemiBold-Latin1.woff2` | 22,260 | `fff0ab3a88b0b4aa0b693e4f0201359a15183b08e3fa5696d1918d8f0ade8ad5` |
| `IBMPlexSans-SemiBold-Latin2.woff2` | 15,624 | `2bc85a8e53540c4c211f7af36e806bb045ba7da8946bb221d87144c12c83d577` |
| `IBMPlexSans-SemiBold-Pi.woff2` | 7,824 | `768421433d850d3a30118dddf05972625d99ee49bc32c5a8fd26bbe020c4d0f9` |
| `IBMPlexMono-Regular-Latin1.woff2` | 17,544 | `e8993d946649b9d01abb1ed06d574b19d8ea3e66b5c3948602db335c44c18e56` |
| `IBMPlexMono-Regular-Latin2.woff2` | 13,260 | `8c5e48e4c6ee60346744db62fd8bbf530c21e531e1a1498c3a410ed8f7d3986e` |
| `IBMPlexMono-Regular-Pi.woff2` | 13,780 | `b8002770aa636f544ba43e124da6a227301769754f295eae26e16475b469c767` |
| `OFL.txt` (= `package/LICENSE.txt`, identical in both packages) | — | `37784b44044a4ffd9256702b7c0982c37e5c8887ba90c6dca0479aea93dc898d` **after CRLF→LF** (IBM ships CRLF; `.gitattributes` `* text=auto eol=lf` normalises it) |

**Weights.** 400/500/600 are exactly `--font-weight-regular/medium/semibold`, spec 257's
closed scale (`font-bold` does not compile). Mono ships 400 only: all ten `font-mono`
call sites are management-web and none of them sets a weight.

**Subsets.** `Latin1` + `Pi` cover every non-ASCII character the product renders (spec §1).
`Latin2` covers Latin Extended-A for operator-entered names. `Latin3` (Vietnamese),
`Cyrillic` and `Greek` are deferred (spec §3). With `unicode-range`, a browser downloads
`Latin2`/`Pi` only on a page that renders one of their characters.

**Licence obligations (OFL §2).** The copyright notice and licence travel inside every
woff2's `name` table (IDs 0, 13, 14), which OFL §2 accepts ("machine-readable metadata
fields"). So `dist/` needs no licence file. `OFL.txt` is kept beside the bytes in the
repository.

**`.gitattributes`**: add `*.woff2 binary` to the "Explicit binaries" block. Detection by
NUL byte already works. The explicit line documents the category, as `*.png` does.

## 2. `@font-face` and the token stacks

### 2.1 Import route: from JavaScript, not through Tailwind (spec §1 finding 1)

Each app's `src/main.tsx` gains, as its first CSS import:

```ts
import '@smart-sentinel-eye/shared/ui/fonts/fonts.css';
```

`apps/shared/package.json` `exports` gains `"./ui/fonts/*": "./src/ui/fonts/*"`, next to
`./ui/tokens/*`.

**It must not be `@import`ed from `index.css` or `tokens.css`.** `@tailwindcss/postcss`
4.3.3 inlines the import without rebasing `url()`, and `vite build` then ships
`url(./IBMPlex….woff2)`, a 404 that only the production build has. Measured, spec §1.
Build fact B1 catches it, and `fonts.css`'s header says so in one sentence.

### 2.2 `apps/shared/src/ui/fonts/fonts.css`

The header comment covers: ADR-0147; provenance (the two packages and versions, "copied
unmodified, see S1"); why this file is imported from `main.tsx`; why no `local()`
appears in a Plex rule; why `unicode-range` is IBM's, per file; the fallback derivation,
with a pointer to fact S4; and the declaration-order rule in §3.3.

**Twelve Plex faces**, one per file:

```css
@font-face {
  font-family: 'IBM Plex Sans';
  font-style: normal;
  font-weight: 400;
  font-display: swap;
  src: url('./IBMPlexSans-Regular-Latin1.woff2') format('woff2');
  unicode-range: /* verbatim from package/fonts/split/woff2/IBMPlexSans-Regular.css, "Subset: Latin1" */;
}
```

- `unicode-range` is **copied from IBM's CSS for that exact file**. The Mono ranges differ
  from the Sans ranges (Mono `Pi` adds `U+2400-2421`, `U+2500-259F` and others), so no
  range is shared between families. Fact S2 checks every range against its file's cmap.
- **No `local()`**, although IBM's CSS has `local("IBM Plex Sans"), local("IBMPlexSans")`.
  A kiosk with some other Plex build installed would render that build instead, which is
  the fleet disagreement ADR-0147 exists to end. Fact G4 bans mixing `local()` and `url()`.
- An `@font-face` family name **shadows** an installed font of the same name. So spec 257's
  warning ("naming Plex without `@font-face` makes machines with Plex installed disagree")
  is discharged by this file existing.

### 2.3 `tokens.css`

```css
--font-sans:
  'IBM Plex Sans', 'IBM Plex Sans Fallback', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto,
  'Helvetica Neue', 'Noto Sans', Arial, sans-serif, 'Apple Color Emoji', 'Segoe UI Emoji',
  'Segoe UI Symbol', 'Noto Color Emoji';
--font-mono:
  'IBM Plex Mono', 'IBM Plex Mono Fallback', ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas,
  'Liberation Mono', 'Courier New', monospace;
```

The tail of each stack stays **verbatim** (Prettier may rewrap it). The comment above the
two tokens is replaced: it names ADR-0147, says the faces are declared in
`../fonts/fonts.css`, and says the system tail is the last-resort path for a glyph outside
every shipped range. Tailwind's preflight resolves `html` and `code/kbd/samp/pre` through
`--default-font-family` / `--default-mono-font-family`, which cite these tokens. So body
text, form controls (`font: inherit`) and the wall's overlay labels (which set only
`fontWeight: 600`) all inherit Plex with no component edit.

## 3. Fallback metrics (US2)

### 3.1 Tool: `@capsizecss/core` 4.1.3 `createFontStack` + `@capsizecss/metrics` 4.3.0

These are chosen over fontpie, fontaine and a hand calculation. Capsize is the metrics
database the Next.js font pipeline uses. `createFontStack` emits exactly the four
descriptors this spec needs, and both packages are pure data plus arithmetic, runnable
in the vitest node environment. They are **devDependencies of `apps/shared`, pinned
exact, used only by fact S4**. Nothing ships them. The CSS carries literal percentages;
capsize only proves them.

The metrics entry points are **case-sensitive on Linux CI**: `@capsizecss/metrics/iBMPlexSans`,
`…/iBMPlexSans/500`, `…/iBMPlexSans/600`, `…/iBMPlexMono`, `…/arial`, `…/arial/700`,
`…/courierNew` (the package's `./*` export maps to `entireMetricsCollection/*/index`).

### 3.2 The values (computed 2026-09-26 from those versions; S4 recomputes them)

The formula is capsize's: `size-adjust = (plex.xWidthAvg/plex.upm) / (fb.xWidthAvg/fb.upm)`.
Each override = the Plex metric / upm / size-adjust.

| Face (`font-family` / `font-weight`) | `src` | size-adjust | ascent-override | descent-override | line-gap-override |
|---|---|---|---|---|---|
| `IBM Plex Sans Fallback` / 400 | `local('Arial'), local('ArialMT'), local('Liberation Sans'), local('LiberationSans'), local('Arimo')` | 101.1663% | 101.3184% | 27.183% | 0% |
| `IBM Plex Sans Fallback` / 500 | same as 400 | 103.4094% | 99.1206% | 26.5933% | 0% |
| `IBM Plex Sans Fallback` / 600 | `local('Arial Bold'), local('Arial-BoldMT'), local('Liberation Sans Bold'), local('LiberationSans-Bold'), local('Arimo Bold')` | 97.2956% | 105.349% | 28.2644% | 0% |
| `IBM Plex Mono Fallback` / 400 | `local('Courier New'), local('CourierNewPSMT'), local('Liberation Mono'), local('LiberationMono'), local('Cousine')` | 99.9837% | 102.5167% | 27.5045% | *(capsize emits none: Courier New's lineGap is 0)* |

The Linux names sit in the lists because Liberation Sans and Mono and Arimo and Cousine
are **metric-compatible clones** of Arial and Courier New (same advances, same vertical
metrics). A Linux wall resolves them, and the same overrides hold. The Plex line box is
1.300 em. Each fallback's is `(ascent + descent + gap) × size-adjust` = 1.300 em.

### 3.3 Digits: a second fallback face per Sans weight (measured, not in capsize)

Capsize's `xWidthAvg` is weighted by English letter frequency. Measured against
`C:/Windows/Fonts/arial.ttf`/`arialbd.ttf` with the size-adjust above, prose strings land
within **−0.4 % … +2.1 %** of Plex. But **`12:34:56.789` lands −5.6 % (400) and −8.6 %
(600)**. Plex's digits all advance 600/1000 (spec §1 finding 4). Arial's and Arial Bold's
all advance 1139/2048. This product is "numbers in columns" (ADR-0147), and an
auto-width table column of timestamps would shift on swap.

Add, **declared after** each Sans fallback face of the same weight (for overlapping ranges,
a later-declared face is tried first):

```css
@font-face {
  font-family: 'IBM Plex Sans Fallback';
  font-style: normal;
  font-weight: 400; /* and 500, and 600 with the Bold src list */
  src: local('Arial'), …;            /* same src list as that weight's fallback */
  unicode-range: U+0030-0039;
  size-adjust: 107.8841%;            /* 0.600 / (1139/2048) */
  ascent-override: 95.0094%;         /* 1.025 / size-adjust */
  descent-override: 25.4903%;        /* 0.275 / size-adjust */
  line-gap-override: 0%;
}
```

The line box is still 1.300 em and the ascent is still 1.025 em, so digits sit on the
same baseline in the same box. The digits are drawn 7.9% larger than the surrounding
fallback letters. That is a fallback, visible for tens of milliseconds, and the
alternative is a column that moves. Mono needs none: Plex Mono and Courier New both
advance 0.6 em per glyph (measured 0.00% on every sample).

The Arial digit advance, 1139/2048, is a **cited constant** in fact S4b, measured
2026-09-26 from the Windows files and identical for Regular and Bold. Capsize does not
carry per-glyph advances.

### 3.4 The honest limit

Facts S4/S4b prove the declared values are the derived ones. Fact S5 proves the derivation
describes these bytes. E4 proves, in Chromium on the CI font set, that the boxes match.
Nothing here measures CLS on kiosk hardware (spec US2 "Stated gap"). Kerning (Plex has
`GPOS kern`) is outside the advance-sum model, which is one reason E4's width tolerance is
3% and not the ~2% the advance sums suggest.

## 4. Preload (US3)

### 4.1 `apps/shared/src/ui/fonts/fontPreload.ts`

```ts
import type { Plugin } from 'vite';

/** Preloads the named files from this directory, at the URL the stylesheet fetches them from. */
export function fontPreload(files: readonly string[]): Plugin
```

- `configResolved(config)` keeps `config.command` and `config.root`, and **throws** if a
  named file does not exist beside this module. A typo fails the build, not the wall.
- `transformIndexHtml: { order: 'pre', handler }` returns one tag descriptor per file:
  `{ tag: 'link', attrs: { rel: 'preload', href, as: 'font', type: 'font/woff2', crossorigin: '' }, injectTo: 'head' }`.
  - **serve**: `href = '/@fs/' + <absolute posix path, leading '/' removed>`. This is the
    URL Vite's dev CSS rewriting gives the same file (measured on Windows as
    `/@fs/C:/…`; on Linux the result is `/@fs/home/…`).
  - **build**: `href = <posix path relative to config.root>` (e.g.
    `../shared/src/ui/fonts/IBMPlexSans-Regular-Latin1.woff2`). Order `pre` puts the tag in
    before Vite's HTML asset pass, which rewrites it to the same `/assets/…-<hash>.woff2`
    the CSS gets, as a single emitted asset (measured).
- `crossorigin` is mandatory. Fonts are fetched in CORS mode, and a preload without it is
  a second request.
- The file location comes from `new URL(file, import.meta.url)`. Vite's config bundler
  preserves each imported module's original `import.meta.url`, and E3/B4 prove it.
- It needs `vite` types, so `apps/shared` gains devDependency `"vite": "8.2.2"`, the
  version both apps pin. It needs `node:url`/`node:path`, which resolve exactly as they do
  for the existing `tokens.build.test.ts` under the same tsconfigs.
- `base` is `/` in both apps. The plugin does not handle another base, and does not claim to.

### 4.2 Per-app wiring (spec US3 table)

Both `vite.config.ts` import it by relative path, as `tailwind.config.ts` imports
`tailwindTheme`:

```ts
import { fontPreload } from '../shared/src/ui/fonts/fontPreload';
// plugins: [react(), fontPreload(['IBMPlexSans-Regular-Latin1.woff2', 'IBMPlexSans-SemiBold-Latin1.woff2'])]
```

Each call site has a one-line comment stating *why these two* for that app: kiosk, the
screen headings and the overlay labels at 600; management, the sign-in `<h1>` + body.
The two lists are equal today by finding, not by construction. They stay two call sites.

## 5. Dependencies added (all exact pins, `apps/shared` devDependencies)

| Package | Version | Used by |
|---|---|---|
| `@capsizecss/core` | 4.1.3 | S4 |
| `@capsizecss/metrics` | 4.3.0 | S4, S5 |
| `vite` | 8.2.2 | `fontPreload.ts` types |
| `postcss` | 8.5.26 (the version both apps pin) | S2, S4, S4b parse `fonts.css`. pnpm is strict, so the apps' copy is not resolvable from `apps/shared`. |

`typescript` (6.0.3, already a devDependency) is used by S3. The lockfile changes. Nothing
is added to any app's runtime `dependencies`.

## 6. Tests

WOFF2 reading is needed by S2, S4b and S5. A test-support module,
`apps/shared/src/test/woff2.ts`, parses the WOFF2 header and table directory
(UIntBase128 lengths; `glyf`/`loca` transform flags), `brotliDecompressSync`s the table
stream, and exposes `head`, `hhea`, `hmtx` (untransformed in all twelve files, measured)
and a `cmap` code-point set (formats 4 and 12). A scratch reader of about 80 lines proved
this on all twelve files on 2026-09-26. It is test code, with no dependency.

### 6.1 `tests/Architecture.Tests/ExternalFontHostTests.cs` (US1 source authority)

Shape as `ContainerImagePinTests`/`DesignTokenLayerTests`: reads from disk via
`RepositorySource.Root()`/`RepositorySource.RelativePath`, comment-stripped CSS,
forward-slash paths, failure messages that say what to do. The class doc-comment carries
**"bans a category, never a value"** (no host name appears in a regex: *any* absolute URL
is the violation, because §I bans every outbound fetch and fonts are the case that
motivated the guard) and **"what this scan cannot see"**: stylesheets or `FontFace`s built
in TS at runtime, CSS a dependency brings in from `node_modules`, and `url()`s assembled
from variables. B3 (built output) and E1 (network) are the authorities for those, as
`AppHostContainerImagePinTests` is for what `ContainerImagePinTests` cannot see.

Scanned: every `*.css` under `apps/` excluding `node_modules/`, `dist/` and `.vite/`, plus
`apps/*/index.html`.

| Fact | Asserts | Develop |
|---|---|---|
| G1 `No_stylesheet_under_apps_references_an_absolute_url` | no `url(…)` or `@import` target starting `http:`, `https:` or `//` (quoted or bare) | vacuous green → counterfactual |
| G2 `No_index_html_link_loads_from_an_absolute_url` | no `<link … href="http…|//…">` in `apps/*/index.html` | vacuous green → counterfactual |
| G3 `Every_font_face_url_names_a_file_that_exists` | each `@font-face` `url()` is relative and resolves (relative to its stylesheet) to an existing file; **plus the self-check: at least one such `url()` was found, else "the scan is broken, not the code"** | **red** (none found) |
| G4 `No_font_face_mixes_local_and_url_sources` | an `@font-face` whose `src` has a `url()` has no `local()` | vacuous green → counterfactual |

### 6.2 `apps/shared/src/ui/fonts/fonts.test.ts` (node environment; US1, US2)

| Fact | Asserts | Develop |
|---|---|---|
| S1 | each of the 12 woff2 has §1's SHA-256; `OFL.txt` matches after CRLF→LF | red (absent) |
| S2 | for each Plex `@font-face` in `fonts.css` (parsed with `postcss`, §5), every code point in `unicode-range` is in the cmap of the file its `url()` names | red |
| S3 | every non-ASCII character in a string literal, template literal or JSX text of a non-test `.ts/.tsx` file under `apps/{kiosk-web,management-web,shared}/src` (TypeScript compiler API, **not** a regex, so comments cannot count) falls in the `unicode-range` of some `IBM Plex Sans` weight-400 face; the scan must find ≥ 1 such character (self-check; 11 distinct today) | red |
| S4 | the four general fallback faces' (those without `unicode-range`) descriptors equal `createFontStack([plex, fallback]).fontFaces`' descriptors for the §3.2 pairs, to 4 decimals, **the same set of descriptors** (so Mono has no `line-gap-override`) | red |
| S4b | for each Sans weight: all ten digits in the shipped `Latin1` file share one advance (the premise of §3.3); a `unicode-range: U+0030-0039` fallback face exists for that weight, **declared after** the general one, with size-adjust = `digitAdvance/upm ÷ 1139/2048` (cited constant) and ascent/descent/line-gap = hhea ÷ size-adjust, to 4 decimals | red |
| S5 | each shipped file's `head.unitsPerEm` and hhea ascender/descender/lineGap equal `@capsizecss/metrics`' entry for its family and weight | red |

### 6.3 `apps/{kiosk-web,management-web}/src/styles/fonts.build.test.ts` (US1, US2, US3; built output authority)

These are one per app, for the reason spec 257 plan §5.3 gives: two configs, and two
chances to wire it wrong. `@vitest-environment node`. `beforeAll` (180 s) runs the **real**
`vite build` from the installed `vite` with `root` = app root, the app's own
`vite.config.ts`, `logLevel: 'silent'` and `build.outDir` = a fresh `mkdtemp` directory
(removed in `afterAll`). If the programmatic `build()` cannot run inside the vitest worker,
the permitted fallback is spawning `vite build --outDir <tmp>` with `node:child_process`.
Either way, it is the same build `pnpm build` runs.

| Fact | Asserts | Develop |
|---|---|---|
| B1 | built CSS has ≥ 12 `@font-face`, every `url()` is `/assets/…woff2` (not `data:`, no scheme, no `//`), and each exists in outDir | red |
| B2 | each of the 12 committed woff2 files is byte-identical to some file in `outDir/assets` | red |
| B3 | no built `.css` or `.html` contains `url(`/`@import`/`href=`/`src=` with an `http:`, `https:` or `//` target | vacuous green → counterfactual |
| B4 | `index.html` has exactly two `<link rel="preload" as="font">`, each with `type="font/woff2"` and `crossorigin`; their hrefs equal the `url()`s of the `IBM Plex Sans` 400 and 600 faces whose `unicode-range` contains U+0041 | red |
| B5 | built `--font-sans` starts `'IBM Plex Sans', 'IBM Plex Sans Fallback',` / `"IBM Plex Sans","IBM Plex Sans Fallback",` (normalise quotes and whitespace); `--font-mono` likewise | red |

### 6.4 `e2e/self-hosted-fonts.spec.ts` (runtime authority; `chromium` project, management-web)

It runs on the **sign-in screen**. No Keycloak round-trip is needed: the screen renders a
SemiBold `<h1>` and a Regular button (spec §1 finding 5). A fresh context per test gives a
cold cache. Listeners attach before `page.goto('/')`.

| Fact | Asserts | Develop |
|---|---|---|
| E1 | every `font`-type request goes to the page's origin, **and** ≥ 1 `IBMPlexSans-Regular-Latin1` request finished `200` | red (second clause) |
| E2 | after `document.fonts.ready`: `getComputedStyle(document.body).fontFamily` starts with `"IBM Plex Sans"`; `document.fonts.check('400 16px "IBM Plex Sans"')` and `('600 16px "IBM Plex Sans"')` are true | red |
| E3 | there are exactly two `link[rel=preload][as=font]`; each absolute `href` is a font URL the page requested, and each such URL was requested **exactly once** | red |
| E4 | for Sans 400/500/600 and Mono 400: `document.fonts.load` the Plex face and the fallback, **assert the fallback resolved to a loaded face** (fail loudly: "no Arial/Liberation on this machine"). Then, at 16px and `line-height: normal`, a prose sample (`'This screen is no longer authorized'`) and a digit sample (`'12:34:56.789'`) differ by ≤ 1px in height, ≤ 1px in baseline offset (a zero-size inline-block probe) and ≤ 3% in width | red (no fallback face) |

The wait is by condition (`document.fonts.ready`, `expect.poll`), never by count (ADR-0150).
The CI runner's Chromium comes from `playwright install --with-deps chromium`, whose Ubuntu
dependency set includes `fonts-liberation`. **[ASSUMPTION]**: E4's "fallback loaded" assertion
turns it into a loud failure if that stops being true.

### 6.5 Counterfactuals (4a; each quoted in the PR, then reverted)

1. G1 + B3: `@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans');` at the
   top of `apps/kiosk-web/src/styles/index.css`. Run G1 and then kiosk B3, and both must go
   red naming the URL.
2. G2: `<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />` in
   `apps/management-web/index.html`.
3. G4: a scratch CSS file under `apps/shared/src/ui/fonts/` with IBM's own rule
   (`src: local("IBM Plex Sans"), url("x.woff2")`).
4. After 4b (4a re-run by the orchestrator in phase 5, or appended to the PR): S4 with one
   percentage changed by 0.0001, and S1 with one byte of a copy changed. Each must go red.

## 7. Expected visible change (the Phase-5 checklist)

- Every text surface in both apps changes face from the system stack to IBM Plex Sans.
  Glyph shapes change: the single-storey `g`, the angled terminals. **No box should
  move** relative to spec 257's screenshots beyond letter width, because sizes, leading
  and tracking are the tokens' and are unchanged.
- Predicates, identifiers and the system-variable value are in Plex Mono.
- The wall's overlay labels are Plex Sans SemiBold.
- Line lengths shift slightly: Plex prose runs about 1% wider than Arial at the same size.
  A label that wrapped exactly at a boundary may wrap differently. That is the face
  change itself, not a swap shift.

## 8. Commit sequence (each commit builds on its own, ADR-0087)

1. `docs(261): specify, plan and task the self-hosted font pipeline`
2. `test(fonts): pin the self-hosted font pipeline red-first`: the woff2 reader, S1–S5,
   B1–B5 ×2, G1–G4, E1–E4, plus the four devDependencies (the tests need them to
   compile). It compiles, and it is red.
3. `feat(fonts): self-host IBM Plex with metric-matched fallbacks`: the bytes, `OFL.txt`,
   `.gitattributes`, `fonts.css`, the `exports` entry, both `main.tsx` imports and the
   `tokens.css` stacks. S*, G*, B1–B3, B5, E1, E2 and E4 go green.
4. `feat(fonts): preload the first screen's faces at the URL the stylesheet uses`:
   `fontPreload.ts` and both `vite.config.ts`. B4 and E3 go green.

## 9. Verification (Phase 5)

Spec §7, steps 1–7, on the live stack. The note records the two preload URLs observed in
dev and in build, and a screenshot pair (fonts blocked vs. loaded) of the management
sign-in screen and a kiosk tile with a label.

## 10. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | `import.meta.url` in `fontPreload.ts` is wrong once Vite bundles `vite.config.ts` | Vite rewrites it per original module. E3 (dev) and B4 (build) fail if not. |
| R2 | Programmatic `vite build` inside vitest fails (nested Vite) | Spawn `vite build` instead (§6.3). The assertion list is unchanged. |
| R3 | CI Chromium lacks Liberation fonts | E4 asserts the fallback loaded and fails naming the cause. It never silently compares Plex to a serif default. |
| R4 | Kerning pushes a width delta past 3% | E4's samples are measured at ≤ 2.1% without kerning. If E4 is red on width after 4b, **stop and report**. Do not widen the tolerance (ADR-0144: a gate is not weakened to reach green). |
| R5 | Prettier reflows `unicode-range` lines | Formatting cannot change a code point. S2 parses the ranges, not text. |
| R6 | Spec number collides again | Re-list worktrees and branches immediately before the PR (spec header). |
