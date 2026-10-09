# Spec 321: The rule one keyboard test held

**Issue:** #2374 · **ADR:** ADR-0169 (the decision this enforces), ADR-0036 (enforce rules, advise
preferences), ADR-0139 (red first), ADR-0150 (precedent: an ADR enforced by an ESLint rule in this same
config, proved by a `scripts/*.test.mjs` guard) · **Lane:** autonomous (ADR-0144)

## 1. Problem

ADR-0169 decides that a component under `apps/shared/src/ui/composites/**` renders without a Redux
`<Provider>`, and names its enforcement: *"an architecture test asserting that no file under
`ui/composites/**` imports `react-redux` or `@reduxjs/toolkit`."* That test does not exist. The rule is
held today by one assertion in a keyboard-handling suite
(`apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx:693`), so a refactor of that suite removes
the only enforcement with nothing failing. This spec builds the enforcement ADR-0169 names — nothing
wider.

## 2. User story

### US1 (P1) — A composite that imports the store fails the build with a named reason

As a developer adding or changing a shared composite, when I import `react-redux` or `@reduxjs/toolkit`
into a composite, `pnpm lint` fails and tells me ADR-0169 is why — so the rule no longer depends on one
unrelated test surviving.

## 3. Acceptance scenarios

```gherkin
Scenario: a composite importing react-redux is refused
  Given a non-test file under apps/shared/src/ui/composites/
  When it contains `import { useSelector } from 'react-redux'`
  Then ESLint reports an error from no-restricted-imports
  And the message names ADR-0169 and says to pass data and callbacks as props

Scenario: a composite importing @reduxjs/toolkit, or a subpath of it, is refused
  Given a non-test file under apps/shared/src/ui/composites/
  When it imports from '@reduxjs/toolkit' or '@reduxjs/toolkit/query/react'
  Then ESLint reports an error naming ADR-0169

Scenario: re-exports and type-only imports are refused too (conflict: the ADR says "imports", not "value imports")
  Given a non-test file under apps/shared/src/ui/composites/
  When it contains `export { Provider } from 'react-redux'` or `import type { Store } from '@reduxjs/toolkit'`
  Then ESLint reports an error naming ADR-0169

Scenario: the ADR's stated exemptions stay exempt (bad request: the rule must not over-reach)
  Given a file under apps/shared/src/api/ or apps/shared/src/ui/primitives/
  When it imports from '@reduxjs/toolkit' or 'react-redux'
  Then ESLint reports no ADR-0169 error

Scenario: a composite's test may build a store around it
  Given apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx
  When it imports configureStore and Provider (as it does on develop, spec 157)
  Then ESLint reports no ADR-0169 error

Scenario: develop is clean under the rule
  Given develop's apps/shared/src
  When `pnpm --filter @smart-sentinel-eye/shared lint` runs with the rule in place
  Then it exits 0 with no edits to any composite
```

Auth: N/A — a build-time gate, no runtime surface.

## 4. Requirements

- **FR-001** The rule applies to `apps/shared/src/ui/composites/**/*.{ts,tsx}` excluding
  `**/*.test.{ts,tsx}`.
- **FR-002** Refused specifiers: `react-redux`, `@reduxjs/toolkit`, and any subpath of either
  (`react-redux/*`, `@reduxjs/toolkit/*`). Type-only imports and `export … from` are refused alike.
- **FR-003** Severity `error`, so `pnpm lint` (`--max-warnings 0`, run by CI's frontend job) fails.
- **FR-004** The message names ADR-0169 and the constructive alternative (props; the query in the
  feature-level container).
- **FR-005** A guard test lints through the **shipped** `apps/shared` config (not a copy of the rule) and
  proves both directions: must-flag and must-not-flag (§3). It runs under `pnpm test:guards`, which CI's
  `pnpm test` already invokes.
- **FR-006** No composite, no existing test, and `OverlayEditorKeyboard.test.tsx:693` are edited
  (ADR-0169 Consequences keeps that test as documentation of intent).

## 5. Out of scope, and a gap this spec surfaces rather than closes

The ADR's mechanism (no `react-redux` / `@reduxjs/toolkit` import) is **narrower than its decision**
(renders without a `<Provider>`). Three composites on develop already call RTK Query hooks imported from
`apps/shared/src/api/**`, which need a store, yet import neither package:

- `BackdropControls.tsx:2` — `useListAllCameraChoicesQuery`
- `CameraViewer.tsx:2` — `useGetStreamQuery`
- `FrameGrabber.tsx:3` — `useGetStreamQuery`

So ADR-0169's consequence *"a new composite that reaches for a query hook fails the build"* is **not**
delivered by the mechanism it names. Widening the rule to ban value imports of `*.api` hooks would be red
on develop, would need three behaviour-preserving refactors, and would decide something ADR-0169 does not
say (its api/ exemption covers *defining* endpoints, not composites *calling* them). That is a decision
for a human, and the lane may not amend an ADR (ADR-0144). **This spec builds the mechanism as written and
files the gap as a follow-up issue (tasks T040), without `agent:ready`.**

Also out of scope, stated so no one reads the rule as covering them: dynamic `import('react-redux')` and
`require()` (`no-restricted-imports` sees static specifiers only; population zero on develop); an
`eslint-disable` comment (the guard asserts none exists for this rule under composites today — see plan
§3 — but cannot prevent a future one beyond review).

**Bookkeeping, not this spec's to fix:** ADR-0169's `Status` still reads `Proposed` on develop although the
issue records it adopted via PR #2791. Flagged for the human who owns the ADR.

## 6. Locked tech choices

ESLint flat config (`apps/shared/eslint.config.js`), core rule `no-restricted-imports`; guard on
`node:test` + the `eslint` Node API, mirroring `scripts/settle-rule.test.mjs` (ADR-0150) and
`scripts/lint-scope.test.mjs`. No new dependency.

## 7. Phase 4a colour

**Behaviour-changing → RED.** A new enforced gate. The guard's must-flag tests must be observed failing
(0 ADR-0169 problems where they assert ≥1) against develop's config before T020 adds the rule. The
must-not-flag tests will pass on that first run and prove nothing until must-flag has gone red→green
(same caveat `settle-rule.test.mjs` records).

## 8. Independent end-to-end test procedure

1. On the branch, `pnpm test:guards` — the new guard is green.
2. `pnpm --filter @smart-sentinel-eye/shared lint` — exits 0 (develop clean, §3 last scenario).
3. Counterfactual: add `import { useSelector } from 'react-redux';` to the top of
   `apps/shared/src/ui/composites/Badge.tsx`, re-run step 2 — it fails with the ADR-0169 message; quote
   it in the PR. Revert; step 2 is green again.
4. Same counterfactual with the line added to `apps/shared/src/api/cameras.api.ts` — step 2 stays green
   (exemption holds).

## 9. Latency budget

N/A — build-time lint gate; no runtime code changes, no leg affected.
