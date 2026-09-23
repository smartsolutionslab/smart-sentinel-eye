# Spec 224 — Visible, not merely present

**Issue**: #2304 · **Branch**: `2304-visible-not-merely-present` · **Phase**: 1 (Specify)
**Date**: 2026-09-23 · **Context**: `apps/shared` (frontend package only)
**Engineer**: `frontend-engineer` · **Reviewer**: `frontend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`, Project #13, status Todo
**Phase 4a colour**: **characterisation, observed GREEN** — behaviour-preserving
(constitution §Testing, second obligation). See §6.
**ADRs**: ADR-0037 (the phased workflow), ADR-0074 (two frontend apps over one
shared package — the reason `apps/shared` exists and the reason it drifted from
them), ADR-0144 (the autonomous lane; phase 4a's two colours and its no-exemption
rule), ADR-0139 (rules that fail the build, not the review — cited in §10 for what
this slice deliberately does **not** do), ADR-0036 (smallest possible change;
no lint plugin, no config knob, no new abstraction), ADR-0109 (disjoint files —
§11), ADR-0150 (waiting is a condition, not a count — untouched here, but it is
why `management-web`'s setup file is more than one line and `apps/shared`'s is
exactly one)
**Constitution**: §Testing (the obligation this slice discharges), §IV (the
latency budget — the assertions being strengthened sit on the §IV *observability*
surface; **no leg's timing is touched**, see §8), §II (**does not bind** — no
domain model, no C# at all)
**New ADR needed**: **No.** See §9.

---

## 1. The premise, re-checked by content before specifying

Issue #2304 was filed **2026-09-13** off the whole-project frontend review. It is
ten days old and `apps/shared` moves faster than most of this repo. Every
substantive claim was re-read against the working tree at **`70b52c96`**
(`origin/develop`, fetched 2026-09-23).

**The defect is real and the fix is right. Four of the issue's factual claims are
now wrong, and two of them are wrong in a way that changes what this spec may
claim.** Each is recorded here rather than silently corrected, because a spec
that quietly restates a stale number is how this repository's §IV leg table and
its own Phase-3 board gate drifted.

### 1.1 What still holds

| Issue claim | Status at `70b52c96` |
|---|---|
| `apps/shared/vitest.config.ts` sets `environment: 'node'` with no `setupFiles` | **Holds.** Line 15 exactly; no `setupFiles` key anywhere in the file. |
| `apps/shared/package.json` has no `@testing-library/jest-dom` | **Holds.** Its `devDependencies` carry `@testing-library/react` 16.3.2 and `jsdom` 30.0.1 and no jest-dom. |
| Unlike both apps | **Holds.** `apps/kiosk-web` and `apps/management-web` each pin `@testing-library/jest-dom` **7.0.1** and each load `./src/test/setup.ts`. |
| `getByText` already throws on absence, so `.toBeDefined()` adds nothing | **Holds.** Unchanged in `@testing-library/dom`. |
| `CameraViewer.test.tsx`'s stream-state assertions are on the §IV path | **Holds.** "Reconnecting…", "Stream is offline", "Connecting…" come from `CameraViewer.tsx:471-473`. |

### 1.2 What has drifted — C1: it is **32** sites, not 24

Measured two ways (the repo's own lesson: *the wrong red matches the filed
number*). Total `.toBeDefined()` / `.toBeTruthy()` in `apps/shared/src` is **38**;
of those **32** are DOM-presence assertions and convert, **6** do not.

| File | Converts | Lines |
|---|---|---|
| `src/ui/composites/CameraViewer.test.tsx` | 7 | 142, 161, 201, 215, 254, 259, 280 |
| `src/ui/composites/CameraViewerCameraSwap.test.tsx` | 10 | 423, 475, 493, 518, 545, 546, 627, 628, 651, 652 |
| `src/ui/composites/CameraViewerMedia.test.tsx` | 4 | 255, 279, 385 (multi-line), 389 |
| `src/ui/composites/ErrorBoundary.test.tsx` | 5 | 40, 51, 52, 68, 72 |
| `src/ui/composites/FrameCapture.test.tsx` | 2 | 288, 370 |
| `src/ui/composites/OverlayGeometryFields.test.tsx` | 2 | 381, 395 |
| `src/ui/primitives/ConfirmDialog.test.tsx` | 2 | 46, 114 |
| **Total** | **32** | |

**The six that must NOT convert** — a `.toBeDefined()` on a non-DOM value is out
of scope, and converting one would be a behaviour change, not a matcher swap:

| Site | Subject | Why it stays |
|---|---|---|
| `OverlayEditorKeyboard.test.tsx:113` | `label.compareDocumentPosition(input) & Node.DOCUMENT_POSITION_FOLLOWING` | A bitmask number. `toBeVisible` would reject a non-element. |
| `OverlayEditorKeyboard.test.tsx:567` | an accessible-name **string** | Not an element. |
| `OverlayEditorKeyboard.test.tsx:583` | an `aria-describedby` **string** | Not an element. |
| `OverlayGeometryFields.test.tsx:489` | an `aria-describedby` **string** | Not an element. |
| `api/cameras.api.test.ts:373` | `result.error` (an RTK Query error object) | Node environment, no DOM. |
| `realtime/layoutHub.test.ts:251` | a hub connection **factory** | Node environment, no DOM. |

**Why 24 became 32.** The issue's count is not a miscount; the suite grew.
`CameraViewerCameraSwap.test.tsx` and `CameraViewerMedia.test.tsx` did not carry
these assertions in this quantity on 2026-09-13, and every one of the issue's
seven `CameraViewer.test.tsx` line numbers has shifted by **+7** (135→142,
154→161, 194→201, 208→215, 247→254, 252→259, 273→280). The seven sites
themselves are the same seven.

### 1.3 What has drifted — C2: it is **19** jsdom files, not five

`apps/shared` has **33** test files. `environment: 'node'` in `vitest.config.ts`
is **genuinely global**, and **19** files override it per-file with a
`// @vitest-environment jsdom` pragma on line 1:

```
CameraViewer, CameraViewerAlignment, CameraViewerCameraSwap, CameraViewerMedia,
CameraViewerSamplerWindow, ErrorBoundary, FormErrorSummary, FrameCapture,
OverlayEditorBackdrop, OverlayEditorCharacterisation, OverlayEditorKeyboard,
OverlayEditorUndo, OverlayGeometryFields, OverlayLabelCharacterisation,
OverlayLabelParity, PlaceholderPreviewPanel, useOverlayEditHistory,
Button, ConfirmDialog
```

(A twentieth hit, `overlayLabelStyle.test.ts:7`, is a **comment** explaining why
that file needs *no* pragma. It is not a jsdom file.)

**This changes the fix's shape, and it is the reason C2 is recorded rather than
tidied away.** "Five jsdom files" would have invited a five-file `projects`
split or an `include`-scoped second config. Nineteen jsdom files against
fourteen node files, with the split expressed *per file* and not per directory,
means the only correct wiring is a **single global `setupFiles`** that must load
cleanly under **both** environments — which §1.5 proves it does.

### 1.4 What has drifted — C3: two of the issue's three stated benefits are **wrong**

The issue says neither current form "can distinguish a rendered node from one
that is `display:none`, `aria-hidden`, or covered."

Read from the pinned implementation — `@testing-library/jest-dom` **7.0.1**,
`dist/matchers-3ed9c960.js`, `isStyleVisible` / `isAttributeVisible` /
`isElementVisible` / `toBeVisible`:

```js
function isStyleVisible(element) {
  const {display, visibility, opacity} = getComputedStyle(element);
  return display !== 'none' && visibility !== 'hidden' &&
         visibility !== 'collapse' && opacity !== '0' && opacity !== 0
}
function isAttributeVisible(element, previousElement) { /* … */
  return !element.hasAttribute('hidden') && detailsVisibility
}
```

`toBeVisible()` checks, for the element **and every ancestor**: in-document,
computed `display`, computed `visibility`, computed `opacity`, the `hidden`
attribute, and `<details open>`. It checks **nothing else**.

- **`display:none`** — the issue is **right**.
- **`aria-hidden`** — the issue is **wrong**. The string `aria-hidden` does not
  occur in the matcher bundle at all. A node hidden only from the accessibility
  tree is `toBeVisible()`.
- **"covered"** — the issue is **wrong**. jsdom computes no layout; there is no
  occlusion, hit-testing or z-order check anywhere in the matcher.

**And a fourth limit the issue does not mention**, which matters more than either
correction: jsdom loads **no stylesheet**, so a Tailwind utility class is inert.
`class="hidden"` computes to `display: block` under test. `toBeVisible()` cannot
catch a Tailwind-hidden node in this suite, today or ever.

**So the acceptance criterion this spec may claim is narrower than the issue's
framing**, and §3's scenarios are written to the narrow claim.

### 1.5 What has drifted — C4: `apps/shared` is no longer where the issue left it

Two facts, both established by running code rather than reading it (§2), both of
which the issue could not have known:

- The 19/14 jsdom/node split means the setup file loads in **node** test files
  too. Verified: it does so without error.
- `apps/shared` tests import `{ describe, expect, it }` **explicitly from
  `vitest`** despite `globals: true`. The jest-dom type augmentation targets
  `vitest`'s `Assertion` interface, so this is the case it supports; no
  `vitest/globals` type entry is needed in `apps/shared/tsconfig.json`.

---

## 2. The counterfactual — proving the change is worth making

**This is the whole point of the slice, so it is not taken on faith.** A
throwaway suite was run in `apps/kiosk-web` (which already has jest-dom 7.0.1 and
jsdom wired), rendering the exact shape of `CameraViewer.tsx`'s `ViewerOverlay` —
a status `<span>` inside an absolutely-positioned wrapper `<div>` — and asserting
both matchers against it. Run 2026-09-23, `npx vitest run`, 5/5 passed:

```
 ✓ display:none on the WRAPPER — toBeDefined passes, toBeVisible must fail 155ms
 ✓ hidden attribute on the WRAPPER — toBeTruthy passes, toBeVisible must fail 13ms
 ✓ opacity:0 on the WRAPPER — toBeDefined passes, toBeVisible must fail 9ms
 ✓ CONTROL: a Tailwind class alone does NOT hide it in jsdom (no stylesheet) 7ms
 ✓ CONTROL: aria-hidden is NOT checked by toBeVisible 7ms

 Test Files  1 passed (1)   Tests  5 passed (5)
```

Each of the first three asserted the *current* matcher green and the *new* matcher
throwing, on the same DOM. The two controls asserted the two limits §1.4 records.
The file was deleted after the run; it is reproduced in §6.4 so a later reader can
re-run it.

**Conclusion.** `toBeVisible()` is strictly stronger than `toBeDefined()` on all
32 sites, and the strengthening is real but bounded: it catches an *inline* or
*attribute* hiding of the status overlay or any ancestor, and it does not catch a
class-driven or aria-driven one. That is worth having — `CameraViewer.tsx:429`
already renders `OverlayLabel` with a computed inline `style` object, so inline
styling is the idiom this component reaches for, and a future fade or
`display:none` transition on `ViewerOverlay` is exactly the regression these
seven §IV assertions would otherwise sleep through.

**A second counterfactual, on the wiring rather than the matcher.** A
`// @vitest-environment node` test was run in `apps/kiosk-web` with its jest-dom
setup file active, asserting `typeof globalThis.document === 'undefined'`. It
passed. So a single global `setupFiles` is safe across the 19/14 split and no
`projects` split is needed.

---

## 3. User stories

### P1 — An operator's stream state is asserted as *seen*, not as *present* (the only story)

**As** the engineer who next changes `CameraViewer`,
**I want** the suite to fail when "Reconnecting…" is rendered but hidden,
**so that** a wall showing a silently-blank overlay on the §IV path is caught by
CI rather than by an operator in a fab.

This is the whole slice. There is no P2.

#### Acceptance scenarios (Gherkin)

**AS-1 — happy path: the characterisation holds**
```gherkin
Given apps/shared's 32 DOM-presence assertions currently pass
  And they have been captured passing at 70b52c96 before any change
When @testing-library/jest-dom is added, a setup file is wired,
  and the 32 assertions change matcher from .toBeDefined()/.toBeTruthy() to .toBeVisible()
Then all 32 pass again
  And no assertion's subject or expected value changed
  And the full apps/shared suite's pass/fail counts per file are identical before and after
```

**AS-2 — the strengthening is real (the counterfactual, re-run as the gate)**
```gherkin
Given the converted CameraViewer.test.tsx stream-state assertions
When ViewerOverlay's wrapper <div> is temporarily given style={{ display: 'none' }}
Then at least one converted assertion FAILS with a jest-dom "element is not visible" message
  And the same assertion would have PASSED under .toBeDefined()
```
*(Run as a deliberate, reverted counterfactual during phase 5; its output is the
verification note. It is not committed.)*

**AS-3 — the node-environment files are unharmed (the wiring risk)**
```gherkin
Given apps/shared's 14 test files that run under environment: 'node'
When a global setupFiles importing '@testing-library/jest-dom/vitest' is added
Then those 14 files still pass
  And none of them fails on a missing document, window or Element
```

**AS-4 — bad-request / misapplication: a non-element is not converted**
```gherkin
Given the 6 sites listed in §1.2 whose subject is a string, a number, a factory or an error object
When the conversion is applied
Then those 6 are left exactly as they are
  And no site anywhere calls .toBeVisible() on a value that is not an HTMLElement
```
*(A `toBeVisible()` on a non-element throws `received value must be an
HTMLElement or an SVGElement` — a red suite, which under the characterisation
colour is a **regression**, not a step.)*

**AS-5 — auth/scope**
```gherkin
Given this slice changes only a frontend test package
Then no endpoint, scope, token, realm or fab authorization is touched
  And no sse.* scope, RequireScope call or Keycloak artefact appears in the diff
```
*(Recorded rather than omitted: §3's scenario set is required to cover auth, and
"N/A" needs to be a finding, not a gap.)*

**AS-6 — conflict: the type augmentation must actually reach the tests**
```gherkin
Given apps/shared/tsconfig.json has no "types" array and includes "src/**/*"
When the setup file is placed at src/test/setup.ts
Then `pnpm --filter @smart-sentinel-eye/shared typecheck` passes
  And `.toBeVisible()` resolves on vitest's Assertion interface, not `any`
```
*(The conflict this guards: a setup file placed **outside** `src` — e.g. at the
package root — would wire the runtime matcher and leave the *types* missing,
giving a green suite and a red `typecheck`. `rootDir: "src"` makes this a real
trap.)*

---

## 4. Independent end-to-end test procedure

Reproducible by a reader with the branch checked out and nothing else.

1. `pnpm install` at the workspace root (picks up the new `apps/shared` devDependency).
2. **Before** (on `origin/develop`, `70b52c96`):
   `pnpm --filter @smart-sentinel-eye/shared test` → record the per-file pass counts.
3. **After** (on the branch): the same command → the per-file pass counts must be
   **identical**.
4. `pnpm --filter @smart-sentinel-eye/shared typecheck` → clean (AS-6).
5. `pnpm --filter @smart-sentinel-eye/shared lint` → clean, `--max-warnings 0`.
6. **The counterfactual (AS-2)**: add `style={{ display: 'none' }}` to
   `CameraViewer.tsx`'s `ViewerOverlay` wrapper `<div>`, re-run step 3, observe
   `CameraViewer.test.tsx` fail with jest-dom's "element is not visible" message,
   **revert**. Quote the failure in the PR body.
7. `grep -rcE "\.(toBeDefined|toBeTruthy)\(\)" --include=*.tsx --include=*.ts apps/shared/src`
   → total **6**, matching §1.2's out-of-scope table exactly.

---

## 5. Locked tech choices

| Choice | Value | Why |
|---|---|---|
| Matcher library | `@testing-library/jest-dom` **7.0.1** | The version both apps already pin. A third version in one workspace is drift, not a decision. |
| Setup file path | `apps/shared/src/test/setup.ts` | Byte-identical location to both apps (ADR-0074's shared-package symmetry), and inside `rootDir`/`include` so AS-6 holds. |
| Setup file content | `import '@testing-library/jest-dom/vitest';` — one line | `kiosk-web`'s setup file exactly. `management-web`'s extra `configure({ asyncUtilTimeout })` is ADR-0150's business and **is not copied**; `apps/shared` has not hit that problem and copying it would be speculative generality (ADR-0036). |
| Wiring | a single global `setupFiles: ['./src/test/setup.ts']` in `vitest.config.ts` | Proven safe across the 19/14 environment split (§2). No `projects` split, no second config. |
| `environment` | stays `'node'`, pragmas stay | Changing the global to `jsdom` would be a behaviour change to 14 files and is not this issue. |
| Replacement matcher | `.toBeVisible()` | The issue's own instruction; strictly stronger, proven in §2. |
| Package manager | `pnpm@10.30.3`, workspace-pinned exact version (no `^`) | Every `apps/*/package.json` entry is exact. |

---

## 6. Phase 4a: colour, and why

**Characterisation — observed GREEN.** Constitution §Testing, second obligation;
ADR-0144's two-colour rule.

This is behaviour-preserving in the strict sense: **no production source file
changes at all.** The diff is one `package.json` devDependency, one new
one-line setup file, one `vitest.config.ts` key, and 32 matcher call sites.

### 6.1 The obligation, stated so phase 4 cannot misread it

1. The 32 covering assertions **exist already** and must be captured **passing**
   at `70b52c96` before any edit — the "written first if they don't exist" clause
   does not fire; they exist.
2. After the change they must pass **unmodified except for the matcher call**.
3. **The permitted edit is exactly**: `.toBeDefined()` → `.toBeVisible()` and
   `.toBeTruthy()` → `.toBeVisible()`. Nothing else on the line moves — not the
   query, not the argument, not the custom failure message
   (`CameraViewerMedia.test.tsx:385` carries one and it stays), not the
   surrounding `await`/`act`.
4. **An assertion that has to be edited to pass is evidence the behaviour moved:
   block, do not adjust.** If any of the 32 goes red, stop and report; do not
   soften the matcher, do not add a `waitFor`, do not change the query.

### 6.2 The known-safe list

Every converted site's target element was read for inline `style` and `hidden`.
The two that carry inline styling are benign: `OverlayGeometryFields.tsx:66`
`FIELD_ALERT_STYLE = { color: '#dc2626', fontSize: 12 }` — colour and font size
only. `FrameGrabber.tsx:105`'s genuinely-hidden `<video>` is **not** an assertion
target. `CameraViewer.tsx`'s `ViewerOverlay` wrapper and `<span>` carry classes
only, and jsdom ignores classes. **All 32 are expected green.**

### 6.3 The one residual risk

Radix-rendered content (`ConfirmDialog.test.tsx:46` `getByRole('alertdialog')`,
`:114`) is portalled and Radix does set `hidden` on *closed* content — but
`getByRole('alertdialog')` only matches an **open** dialog, so the closed case is
unreachable from these two sites. Flagged rather than dismissed: if either goes
red, §6.1 rule 4 applies and it is a finding, not a fix-up.

### 6.4 The counterfactual harness, for re-running

Reproduced so §2's claim is checkable. Place in any package with jest-dom + jsdom
wired, run, **delete**:

```tsx
function Overlay({ wrapper }: { wrapper: React.CSSProperties | { hidden: true } }) {
  const isHidden = 'hidden' in wrapper;
  return (
    <div className="absolute inset-0 flex"
         hidden={isHidden ? true : undefined}
         style={isHidden ? undefined : (wrapper as React.CSSProperties)}>
      <span className="font-medium">Reconnecting…</span>
    </div>
  );
}
// display:none | {hidden:true} | opacity:0 →
//   expect(screen.getByText('Reconnecting…')).toBeDefined()                       // passes
//   expect(() => expect(screen.getByText('Reconnecting…')).toBeVisible()).toThrow() // passes
// class="hidden" and aria-hidden="true" → .toBeVisible() PASSES (the two limits)
```

---

## 7. Out of scope, deliberately

| Not done | Why |
|---|---|
| Converting `queryBy…` + `.toBeNull()` (**50 sites** across 13 files) to `.not.toBeInTheDocument()` | A **different** transform with a different justification. `toBeNull()` on a `queryBy` is already exactly right — it says "no such node" and cannot be weakened by styling. Bundling it would triple the diff for no assertion strength. File separately if wanted. |
| An ESLint guard (`eslint-plugin-jest-dom`'s `prefer-in-document` / `prefer-to-have-text-content`) so the pattern cannot return | Tempting under ADR-0139, and genuinely the durable fix — but it is a **new rule that fails the build**, i.e. new behaviour, which collides with this slice's characterisation colour. It would also flag sites beyond the 32. **Recommended as a follow-up issue**; see §12. |
| Changing `environment: 'node'` to `'jsdom'` globally and deleting the 19 pragmas | Behaviour change to 14 node files, and slower. Not this issue. |
| Copying `management-web`'s `configure({ asyncUtilTimeout: 10_000 })` into the shared setup | ADR-0150's remedy for a problem `apps/shared` has not had. Speculative (ADR-0036). |
| Touching `apps/kiosk-web` or `apps/management-web` | Already correct. |

---

## 8. Latency-budget impact (constitution §IV)

**Which leg: none. No runtime code changes.**

Not "N/A", though, and the distinction is the reason this section is more than a
word. The seven `CameraViewer.test.tsx` assertions are the **test-side
observability of the §IV path** — "Connecting…", "Reconnecting…", "Stream is
offline" are what a `CameraViewer` renders when the *Event → overlay state* leg
(≤ 200 ms) or the *SFU → kiosk decode* leg (≤ 120 ms) is not delivering. This
slice makes those assertions stronger; it changes no budget, no buffer, no timing
and no production byte. §IV's dashboard obligation (ADR-0117) is unaffected —
nothing here is a leg measurement.

---

## 9. Is a new ADR needed? **No**

- **jest-dom** is not a new tech choice — ADR-0074's two apps already pin 7.0.1.
  This slice removes an asymmetry inside an existing decision.
- **`toBeVisible()`** is a matcher, not an architecture.
- Nothing in the constitution or any ADR is amended, weakened or reinterpreted.
- The autonomous lane may not write an ADR (ADR-0144). If phase 4 or 6 finds it
  needs one, that is a **blocked** outcome, not a judgement call.

---

## 10. What this slice does *not* prove

Stated plainly so the PR cannot overclaim:

- It does **not** make the suite catch Tailwind-hidden text (§1.4).
- It does **not** make the suite catch `aria-hidden` (§1.4).
- It does **not** make the suite catch a covered or off-screen node — jsdom has
  no layout.
- It does **not** prevent the weak matcher returning tomorrow; nothing fails the
  build on `.toBeDefined()` after this lands (§7, §12).

What it *does* prove: 32 assertions that could not distinguish rendered from
inline-hidden now can, and `apps/shared` now has the same matcher vocabulary as
the two apps that consume it.

---

## 11. Parallelism (ADR-0109)

**None available, and that is the right answer.** All four edited files interact:
`package.json` → `pnpm install` → `vitest.config.ts` → `src/test/setup.ts` →
the seven test files. The seven test-file edits are individually disjoint but each
is a two-token change; fanning them out would cost more coordination than it saves.
**One engineer, one pass.** See `tasks.md` §Parallelism.

---

## 12. Recommended follow-up (not this slice)

File an issue: *"nothing fails the build when a DOM-presence assertion uses
`.toBeDefined()`"* — add `eslint-plugin-jest-dom` to the three frontend packages
with `prefer-in-document` at `error`. New behaviour, its own red, its own spec.
Without it, §10's last bullet stands and this slice is a one-time cleanup rather
than a fix.
