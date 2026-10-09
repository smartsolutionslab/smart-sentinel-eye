# Plan 321: The rule one keyboard test held

**Spec:** [spec.md](./spec.md) · **Issue:** #2374 · **ADRs:** 0169, 0036, 0139, 0150

## 1. Where it lives

Frontend tooling only — no bounded context, no backend layer, no `Shared.Contracts`, no AppHost change.

| File | Change |
|---|---|
| `apps/shared/eslint.config.js` | One new flat-config block (the rule). |
| `scripts/composite-store-rule.test.mjs` | New guard (`node:test`), picked up by the existing `test:guards` glob. |

No composite, test suite, or ADR is edited.

## 2. Mechanism: an ESLint rule, not a bespoke script or a C# scan — why

Three candidates existed: a `scripts/*.mjs` scanner (the `singleton-versions.mjs` shape), a C#
`Architecture.Tests` regex scan (the `SharedUiDependencyUsageTests` shape), or a scoped
`no-restricted-imports` block. The ESLint rule wins:

- **Precedent in the same file and the same kind of decision.** ADR-0150 is enforced by a
  `no-restricted-syntax` block in `apps/shared/eslint.config.js`; plan 316's FR-006 import boundary is a
  `no-restricted-imports` block in `apps/management-cameras/eslint.config.js`. An import boundary is the
  textbook use of this core rule.
- **AST-accurate for free.** It sees multi-line imports, `import type`, `export … from`, and ignores
  comments and strings — every case a regex scanner must re-derive (and the C# scans needed `SourceMask`
  to approximate).
- **Fails at authoring time** in the editor, not only in CI, and through the existing `pnpm lint` step.
- **Provable against the shipped config.** The guard uses `new ESLint({ cwd: apps/shared })` +
  `lintText` with a virtual `filePath`, exactly as `settle-rule.test.mjs` does, so it tests what CI runs
  rather than an idea of the rule (memory: guards that read the design artefact).

ADR-0169 says "an architecture test"; a lint rule plus a guard proving the rule is the form ADR-0150's
enforcement already took, so this is that ADR's shape, not a new one. No new ADR.

## 3. The rule block

Appended after the main `src/**/*.{ts,tsx}` block and before `prettier` (prettier only turns rules off;
no other block in this config sets `no-restricted-imports`, so nothing later overrides it — the guard's
wiring assertion pins that):

```js
// ADR-0169 — a composite in apps/shared does not assume a Redux store.
{
  files: ['src/ui/composites/**/*.{ts,tsx}'],
  ignores: ['src/ui/composites/**/*.test.{ts,tsx}'],
  rules: {
    'no-restricted-imports': ['error', {
      paths: [
        { name: 'react-redux', message: MESSAGE },
        { name: '@reduxjs/toolkit', message: MESSAGE },
      ],
      patterns: [{ group: ['react-redux/*', '@reduxjs/toolkit/*'], message: MESSAGE }],
    }],
  },
},
```

`MESSAGE` names ADR-0169 and says: take data and callbacks as props; put the query or dispatch in the
feature-level container. `allowTypeImports` stays at its default (`false`) — the ADR says "imports".
Comment above the block states the two limits from spec §5 (static specifiers only; mechanism narrower
than the decision, with the follow-up issue number once T040 files it).

## 4. The guard — `scripts/composite-store-rule.test.mjs`

Inline fixture strings (no fixture files needed — each case is one line). `lintText(code, { filePath })`
with paths under `apps/shared/src/...` that need not exist. Filter messages to
`ruleId === 'no-restricted-imports'`.

1. **must-flag** (one test per case, so a red names the case): virtual
   `src/ui/composites/__probe__.tsx` with each of — `import { useSelector } from 'react-redux'`;
   `import { configureStore } from '@reduxjs/toolkit'`;
   `import { createApi } from '@reduxjs/toolkit/query/react'`; `export { Provider } from 'react-redux'`;
   `import type { Store } from '@reduxjs/toolkit'`. Each: exactly 1 problem, message matches `/ADR-0169/`.
   Also `src/ui/composites/nested/__probe__.ts` (the `**` reaches subdirectories).
2. **must-not-flag**: the `react-redux` + `@reduxjs/toolkit` imports at virtual
   `src/api/__probe__.ts`, `src/ui/primitives/__probe__.tsx`, and `src/ui/composites/__probe__.test.tsx`
   → 0 problems each. Plus an innocuous composite import (`import { useState } from 'react'`) → 0
   (discrimination: a rule matching `*` would fail here).
3. **wiring**: `calculateConfigForFile` on a real composite (`src/ui/composites/Badge.tsx`) reports
   `no-restricted-imports` at severity `error` (`2`/`'error'`) — catches a block later overridden.
4. **no escape hatch in use**: no non-test file under `apps/shared/src/ui/composites/` contains
   `eslint-disable` naming `no-restricted-imports` (population zero today; asserts the directory scan
   yields ≥1 file so a rename cannot make it vacuous — commit 7c9f68ea's lesson).

Red-first expectation on develop's config: tests 1 and 3 red (0 problems / rule undefined); tests 2 and 4
green and not yet evidence (spec §7).

## 5. Verification

Spec §8: guards green, shared lint green on develop's sources, counterfactual in `Badge.tsx` red with the
ADR-0169 message (quoted in the PR), counterfactual in `cameras.api.ts` green. Format: `prettier --check`
on both files (memory: eslint clean ≠ prettier clean).

## 6. Risks

- **Mechanism narrower than decision** (spec §5) — surfaced, filed (T040), not silently widened.
- **Flat-config override** — mitigated by guard test 3.
- **Contention:** `apps/shared/eslint.config.js` is touched by any spec adding a lint rule; rebase
  conflicts are additive blocks, trivially resolved.
