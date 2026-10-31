# Spec 468 — The test helpers every API test rebuilds live in one place

**Feature Branch**: `468-shared-test-helpers`
**Created**: 2026-10-31
**Status**: Draft
**Issue:** #3196 (feature-level; found by the frontend refactoring survey, sibling of #3190).
**ADRs:** ADR-0052 (Vitest + hand-written fakes; no new test framework), ADR-0054 (hand-written
builders — shared helpers stay hand-written, no fixture library), ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** This adds test-support modules beside the existing `shared/src/test/focusRefetch`
and follows the same subpath-export convention.
**Latency budget:** none — test-only; no production module changes (§Scope).
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the migrated
files themselves; they must pass with assertions unmodified.

## Problem

`apps/shared/src/test` offers `setup`, `radixJsdom`, `focusRefetch`, `revocationHarness` and a font
helper, and nothing for the three things almost every API-touching test builds by hand. Measured on
`develop` (2026-10-31), across 206 test files:

- **A JSON `Response` builder.** `function jsonResponse(body: unknown, status = 200): Response` is
  defined in **23 test files** (kiosk-web 1, management-cameras 8, management-event-ingestion 1,
  management-web 13). **22 are byte-identical**:
  `new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })`;
  one fixes the status at 200. A sibling `problemResponse(status)`
  (`jsonResponse({ title: 'PROBLEM', status }, status)`) is defined in **6** of them; an
  `errorResponse(status)` and an empty-body `new Response('', { status })` appear once each elsewhere.
- **A store for a set of RTK Query slices.** `configureStore({ reducer: { [api.reducerPath]:
  api.reducer }, middleware: … })` is hand-built in **19 test files**, while a further ~17 call
  `createApiStore([...])` from `@smart-sentinel-eye/shared/store`.
- **A fetch stub.** `vi.stubGlobal('fetch', fetchMock)` appears **61** times and
  `globalThis.fetch = fetchMock as unknown as typeof fetch` **12** times.

The issue's first estimate ("at least 35" `jsonResponse` copies) also counted other helper names
(`json`, `ok`, `respond`); this spec uses the measured figures above.

## Scope

**In:**
- New test-support modules in `apps/shared/src/test`, exported as
  `@smart-sentinel-eye/shared/test/<name>` subpaths like the existing ones: a JSON/problem
  `Response` builder, a test-store builder, a fetch-stub helper.
- Their own tests, each proved by counterfactual.
- Migration of existing test files to them, **one app per PR**.

**Out (explicitly):**
- Splitting large test files (#3197, blocked on this spec).
- `OverlaysPage` / `LayoutsPage` tests — they wait on #3190, which changes the pages under test.
- Any production (non-test) module, including `createApiStore` itself and `gateway.ts`.
- Changing what a test asserts, adding coverage, or fixing a test that turns out to be wrong
  (a refactor that is also a bug fix is two issues, per CLAUDE.md).

## User Scenarios & Testing

### User Story 1 (P1) — A test author gets a JSON response in one import

As someone writing or reading an API test, I want `jsonResponse` and `problemResponse` imported from
the shared test helpers, so I do not paste (or have to verify) a local copy.

**Why P1:** it is the largest, most uniform duplication (22 of 23 byte-identical) and the lowest risk;
it proves the export convention and the migration loop before the store and fetch stub follow.

**Independent test:** the helper's own test, then one migrated app's suite green and unmodified.

**Acceptance scenarios**

```gherkin
Scenario: the shared builder reproduces the local one exactly
  Given the 22 identical local jsonResponse definitions
  When a test imports the shared jsonResponse instead
  Then its Response has the same status, body text and Content-Type header
  And the test's assertions pass unmodified

Scenario: the default status is 200
  When jsonResponse(body) is called with one argument
  Then the response status is 200

Scenario: a problem response carries the status in body and header
  When problemResponse(404) is called
  Then the response status is 404
  And its JSON body is { "title": "PROBLEM", "status": 404 }

Scenario: a file whose local helper differs keeps it
  Given a test whose local jsonResponse fixes the status at 200, or builds an empty body
  When that file is reviewed for migration
  Then it keeps its local helper
  And a comment states how it differs
```

### User Story 2 (P2) — A test gets a store for its slices without re-deriving the wiring

As a test author, I want one call that gives me a store for the RTK Query slices under test, so the
reducer/middleware wiring is not hand-copied 19 times.

**Why P2:** same payoff, but the 19 copies are not uniform (some prepend a listener middleware, see
`AuditPageFocusRefetch.test.tsx`), so it needs a design decision at plan time.

**Independent test:** the builder's test; then one migrated file whose store used `listenerMiddleware`.

**Acceptance scenarios**

```gherkin
Scenario: the store serves the requested slices
  Given the camerasApi and streamsApi slices
  When the test store is built for them
  Then dispatching camerasApi's endpoints works against it
  And state holds each slice under its reducerPath

Scenario: a prepended middleware is preserved
  Given a test that needs listenerMiddleware ahead of the API middleware
  When it is migrated
  Then the migrated store runs the listener before the API middleware
  And the focus-refetch test's assertions pass unmodified
```

### User Story 3 (P3) — A test stubs `fetch` through one helper that restores it

As a test author, I want to stub `fetch` and have it restored after the test, so a forgotten
`vi.unstubAllGlobals()` cannot leak a stub into the next file.

**Why P3:** the 73 sites differ most in how (and whether) they restore; migrate last, and only the
sites that follow the plain pattern.

**Independent test:** the helper's own test; then one migrated file.

**Acceptance scenarios**

```gherkin
Scenario: the stub is installed and restored
  When a test installs the fetch stub
  Then global fetch is the mock during the test
  And is the original afterwards

Scenario: a site with bespoke fetch handling is left alone
  Given a test that routes by URL or replays a sequence with its own bookkeeping
  When reviewed for migration
  Then it keeps its handling and says why
```

### Edge Cases

- A local helper that differs in headers, status default, or body shape is **kept**, with a stated
  reason; the shared helper is not bent to fit it.
- A migrated test that fails means the helper differed from the local copy in a way not yet seen. Stop
  and record it; do not edit the assertion (phase 4a).
- A new helper imported without a `package.json` subpath export fails the importing app's typecheck,
  not silently.
- The helpers must not be reachable from production entry points (`src/index.ts`).

## Requirements

### Functional Requirements

- **FR-001**: The shared test helpers MUST provide `jsonResponse(body, status = 200)` whose `Response`
  is observably identical (status, body text, `Content-Type: application/json`) to the 22 identical
  local copies.
- **FR-002**: They MUST provide `problemResponse(status)` identical to the 6 local copies.
- **FR-003**: They MUST provide a test-store builder for a list of RTK Query slices that supports a
  caller-supplied middleware prepended before the API middleware.
- **FR-004**: They MUST provide a fetch-stub helper that installs a mock and restores the original.
- **FR-005**: Each is exported as `@smart-sentinel-eye/shared/test/<name>` and MUST NOT be reachable
  from `src/index.ts` or any production entry point.
- **FR-006**: Each helper MUST have its own test, and each test MUST be shown failing against a
  deliberately broken helper (counterfactual) before it is trusted.
- **FR-007**: Migrated test files MUST keep every assertion unmodified; only the local helper
  definition is replaced by an import.
- **FR-008**: A file whose local helper differs MUST keep it and state the difference in a comment.
- **FR-009**: Migration lands **one app per PR**; each commit builds and passes on its own.
- **FR-010**: No production module changes. If one seems needed, stop — that is a different issue.

### Key Entities

- **Shared test helper**: a module under `apps/shared/src/test`, exported on a `test/<name>` subpath.
- **Local helper**: a function defined inside a single test file that a shared helper would replace.
- **Divergent local helper**: a local helper whose behaviour differs and so is kept (FR-008).

## Success Criteria

### Measurable Outcomes

- **SC-001**: After all app PRs, a search for `function jsonResponse(` / `function problemResponse(`
  under `apps/**/*.test.ts*` finds only divergent helpers, each with a comment stating the difference.
- **SC-002**: Hand-built `configureStore` test stores fall from 19 to only those with a stated reason.
- **SC-003**: For every migrated file, the diff shows no changed assertion line (`expect(`,
  `toHaveBeen…`, `toBe…`) — verified by listing assertion lines before and after.
- **SC-004**: Each helper's test fails when the helper is deliberately broken (status default, header,
  restore step), recorded in the PR body.
- **SC-005**: Test count and test names per migrated app are identical before and after.

## Assumptions

- The 22 identical `jsonResponse` copies are identical in effect for every caller; the one differing
  copy is kept (FR-008).
- `exports` in `apps/shared/package.json` is the only wiring a new `test/<name>` subpath needs (the
  `focusRefetch` precedent).
- A new test class may need a `shard-N.filter` entry (a missing entry fails deterministically);
  plan phase checks this.
- #3190 and #3197 do not run in parallel on the same files; if #3190 lands first, the
  `OverlaysPage`/`LayoutsPage` tests are re-surveyed rather than migrated blind.
