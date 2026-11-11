# Feature Specification: Frontend import and export conventions

**Feature Branch**: `492-frontend-import-conventions`

**Created**: 2026-11-11

**Status**: Draft; no clarification markers remain

**Issue**: #3257. **Lane**: supervised (ADR-0037). **Kind**: behaviour-preserving refactor, so characterisation, not red-first (ADR-0139).

**Input**: User description: "Drop the `.js` suffix from relative imports across the frontend apps, settle one import style that an
eslint rule enforces, and give barrels and export shape a rule that an eslint rule enforces. No behaviour change."

**Latency budget (§IV): legs touched: none.** The change moves no runtime behaviour. SignalR (`@microsoft/signalr`) is out of scope by
owner instruction and is not touched.

## Why

Relative imports in the frontend end in `.js` (`import { X } from './X.js'`): 237 in `apps/shared`, 259 in `apps/management-web`,
148 in `apps/kiosk-web`, 28 in `apps/management-cameras`, 5 in `apps/management-event-ingestion` (677 in all, counting static,
dynamic and mock forms). The suffix names the file TypeScript would _emit_, which only matters when `tsc` emits code that Node runs. Here
nothing is emitted (type checking only, Vite bundles), so the suffix tells a reader nothing true about the file on disk, and it makes
every new import a coin toss: `apps/management-web` already mixes in extensionless relative imports. Barrels (`index.ts` re-export
files) and default exports have no written rule, so each author decides.

The people affected are the engineers who read and write this code. The cost is review noise, wrong guesses when moving or renaming a
file, and a style that drifts because nothing holds it.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - Relative imports name the module, not an emitted file (Priority: P1)

An engineer opens any frontend file and every relative import reads `from './X'` or `from '../y/Z'`, with no `.js` and no `.ts`.

**Why this priority**: it is the visible change and the largest by far (677 imports). Everything else builds on it.

**Independent Test**: after the change, no relative import in the frontend source ends in `.js`; the existing unit suites, the type
check, the lint and the production builds of every app give the same results as before.

**Acceptance Scenarios**:

1. **Given** any file under `apps/*/src`, **When** its relative imports are read, **Then** none ends in `.js` or `.ts`/`.tsx`.
2. **Given** the five apps, **When** each runs its unit suite before and after, **Then** the file and test counts are identical and no
   test file or assertion was edited to make it pass.
3. **Given** each app's production build, **When** it runs after the change, **Then** it succeeds, and the shell still loads both
   federated remotes at runtime.
4. **Given** `apps/shared` consumed as a package by the other apps, **When** they import from it, **Then** resolution is unchanged.

---

### User Story 2 - One import style that cannot drift back (Priority: P2)

An engineer who writes `from './X.js'`, or any other relative-import form the rule forbids, gets a lint failure naming the rule.

**Why this priority**: without enforcement the style returns within weeks, as it already mixed once.

**Independent Test**: construct each forbidden form in a scratch file, run the lint, see it fail; remove it, see it pass.

**Acceptance Scenarios**:

1. **Given** a relative import ending in `.js`, **When** lint runs, **Then** it fails with a message that says to drop the suffix.
2. **Given** the rule, **When** lint runs over the whole repository after Story 1, **Then** it reports nothing.
3. **Given** a non-relative import (a package, an alias, an asset such as `./x.css` or `./x.svg`), **When** lint runs, **Then** the rule
   does not fire on it.

---

### User Story 3 - A written rule for barrels and export shape (Priority: P3)

An engineer adding a module knows from a written rule, and learns from lint, whether it may be a default export and whether a barrel
may re-export it.

**Why this priority**: it removes the remaining per-author choice, but it changes nothing a user or a build can see, so it follows the
import change.

**Independent Test**: construct a forbidden export form and a forbidden barrel form in a scratch file, run the lint, see each fail.

**Acceptance Scenarios**:

1. **Given** a module that is not a tool-required entry file, **When** it has a default export, **Then** lint fails and says to use a
   named export.
2. **Given** a tool-required entry file (a Vite or Vitest config, for example), **When** it has a default export, **Then** lint does
   not fire.
3. **Given** a barrel that falls outside the written barrel rule, **When** lint runs, **Then** it fails; the rule is written down in the
   repository's contributor guidance with the reason for it.
4. **Given** the existing barrels and the one existing default export, **When** the rule lands, **Then** each is either brought into
   line or listed with the reason it is allowed, and nothing is left unexplained.

---

### Edge Cases

- A relative import that points at a directory (`'./navigation'` resolving to `./navigation/index.ts`) must keep resolving to the same
  module. It is not a candidate for the suffix rule.
- Imports in `vite.config.ts`, which Node evaluates directly under Vite (for example `./src/app/devProxy.ts`), may need the extension
  because Node is not the bundler. The rule must not break them, and the spec's "no `.ts`" statement covers app source only.
- Dynamic `import('./X.js')`, `vi.mock('./X.js')` and `import.meta.glob` strings carry the same suffix and must change together with
  the static imports, or a mock stops matching its module.
- Test files, type-only imports (`import type`) and re-exports (`export * from './X.js'`) are included.
- A module-federation `exposes` path in a remote's config is a path, not an import, and keeps whatever form the plugin needs.
- A circular import that today works by evaluation order must not change order; reordering imports is not part of this change.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: No relative import, dynamic import, re-export, or `vi.mock` path in `apps/*/src` MUST end in `.js`.
- **FR-002**: Every relative import that resolved to a module before MUST resolve to the same module after.
- **FR-003**: Every existing unit suite in the five apps MUST run unmodified (no test file edited) and give the same number of files and
  tests as before the change.
- **FR-004**: Type check, lint (by exit code), prettier check and the production build of every app MUST pass.
- **FR-005**: The shell MUST still load both federated remotes and `apps/shared` MUST still resolve as a package for the other apps.
- **FR-006**: An eslint rule MUST fail a relative import that ends in `.js`, and MUST NOT fire on package, alias or asset imports.
- **FR-007**: An eslint rule MUST fail a default export from a module that is not a tool-required entry file.
- **FR-008**: The barrel rule MUST be written in the repository's contributor guidance, with its reason, and an eslint rule MUST fail
  a barrel that breaks it.
- **FR-009**: Each enforcement rule MUST be proven by a counterfactual: a constructed violation observed failing, then removed.
- **FR-010**: The existing barrels and the existing default export MUST each be brought into line with the rule or recorded as allowed
  with a reason.
- **FR-011**: No runtime behaviour, bundle entry point or public package export of `apps/shared` MAY change.

### Key Entities

- **Relative import**: a module reference starting `./` or `../` in frontend source, including static, dynamic, re-export and mock forms.
- **Barrel**: an `index.ts` whose job is to re-export other modules.
- **Tool-required entry file**: a file a tool loads by name and expects to default-export its configuration.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: Relative imports ending in `.js` in frontend source: from 677 to 0.
- **SC-002**: Unit test files and test counts in each of the five apps: identical before and after, with zero edited test files.
- **SC-003**: Type check, lint, prettier and production build: pass in all five apps, with exit code 0.
- **SC-004**: Each of the three enforcement rules has one recorded counterfactual (violation fails, removal passes).
- **SC-005**: The shell loads both remotes in the full-stack end-to-end run exactly as before.

## Assumptions

- The change touches the five frontend app directories only; no backend, Aspire or gateway code.
- `moduleResolution` is `Bundler` (or can be made so) in every app, which allows extensionless relative imports; confirmed in the plan
  before any file is changed, together with `apps/shared`'s package export map and the module-federation plugin's resolution.
- `vite.config.ts` and similar files evaluated directly by Node keep the extension they need; they are tool-required entry files.
- The existing default export (one in `management-web`) is, or is not, tool-required; the plan decides which and records why.
- Barrels are not forbidden outright. The rule's exact line (for example, permitted at a package's public entry, not inside an
  application's feature folders) is a plan decision, made from how the seven barrels in `apps/shared` are used.
- Import ordering, path aliases and unused-export removal are not part of this change.
- The comments rule (#3256) is separate; no comment is rewritten here beyond the lines an import edit forces.
