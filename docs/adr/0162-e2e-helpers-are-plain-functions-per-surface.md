# ADR-0162: E2E Helpers Are Plain Functions, One Module Per Surface, Extracted Only Where Duplicated

**Status:** **Accepted**
**Date:** 2026-09-29
**Supersedes:** —
**Superseded by:** —
**Relates to:** ADR-0108 (Playwright; real Keycloak login per test), ADR-0109 (`e2e/support/*` is a contention file), ADR-0036 (smallest change), ADR-0139 / constitution §Testing (refactors stay green)

## Context

`e2e/` holds 30 Playwright spec files (8,029 lines) and `e2e/support/` (16
files, 1,735 lines). Until now nothing decided how e2e code is organised.
ADR-0108 chose the tool and the stack it runs against; ADR-0109 named
`e2e/support/*` as a file set parallel branches must not both edit. Nothing
said where a locator or a multi-step action should live.

Issue #2661 reported the result: locators re-typed from file to file. Spec 288
re-measured it (spec 288 §1), and the picture differs from the issue's in ways
that bound this decision:

- **The densest duplication is session code, not locators.** Across the wall
  specs, `claimsOf` has 4 copies, `signInAsWallDisplay` 3, and
  `openFirstLayout`, `storedAccessToken`, `expireStoredAccessToken` and
  `issuerOf` 2 each. Next come a navigate-and-wait-for-heading pair (61 sites
  in 15 files) and a handful of form flows (`registerCamera`, `fillRuleForm`,
  create-draft sequences).
- **The duplication was deliberate.** `wall-survives-a-process-death.spec.ts`
  and `wall-survives-a-lockout.spec.ts` both say they copied instead of
  extracting, because `e2e/support/*` is an ADR-0109 contention file and
  extraction is "a separate refactor" (ADR-0036). The shared layer already
  exists. Moving code into it was put off.
- **A convention already exists.** `signInAsOperator(page)`,
  `signInToKiosk(page)`, `openFirstLayout(page)` and
  `readManagementAccessToken(page)` are plain exported functions that take a
  `Page`, drive it, and assert only arrival.
- **Many tests do not run on the test's own `page`.** 14 of 30 specs launch
  contexts or pages of their own: persistent profiles that outlive a process
  kill, and kiosk-plus-operator pairs.
- **Setup and teardown are already solved.** The `seed` and `cleanup`
  Playwright projects pair them. No `test.extend` exists anywhere in `e2e/`.
- **Every test signs in through the real Keycloak form** (ADR-0108), and that
  is part of what the suite proves.

Playwright's documentation presents Page Object classes and `test.extend`
fixtures as complementary: POM classes, optionally exposed through fixtures.
Most of the fixture advantages it lists (setup and teardown together,
on-demand initialisation, composition) address setup and teardown, which the
projects already cover here.

## Decision

**1. Shape.** Shared e2e code is **plain exported functions** that take the
`Page` (or `BrowserContext`) they drive as their first argument. No classes,
no `test.extend` fixtures, and no change to the `test` that specs import from
`@playwright/test`.

**2. Location: one module per surface in `e2e/support/`.** A surface is one
app area or one session kind: `wall-session.ts`,
`management-navigation.ts`, a module per console section's flows, and the
existing `sign-in.ts`, `kiosk-session.ts` and `management-session.ts`. No new
top-level directory. Module names must not match any project's `testMatch`
and must not end in `.spec.*`, `.test.*`, `.setup.ts` or `.teardown.ts`.

**3. Extraction threshold.** Code moves to `support/` when it has **a second
caller in a different file**. A helper used by one spec file stays in that
file (`in-flight-focus.spec.ts:holdWrites` already states this rule for itself).
Repetition inside a single file is that file's business: a local constant or
function, not a shared module. This ADR does **not** mandate rewriting the 30
existing specs to a uniform style.

**4. What a helper may assert.** A helper may assert **arrival**: a page
heading, a populated list, a stored grant, a created row. It must **not**
assert the behaviour a test exists to prove. Red-line assertions,
counterfactuals and measurements stay in the spec file, next to the comments
that explain them. Failure messages passed as `expect(…, 'message')` are part
of a helper's contract. Where callers need different wording, budgets or URLs,
those are parameters. A caller's variant is never silently unified away.

**5. Real sign-in stays per test.** Sharing authenticated state across tests
(`storageState`) is not adopted. It would change what ADR-0108's e2e proves.
Adopting it needs its own ADR.

**6. How a move out of a spec is characterised.** Moving code out of a spec
is behaviour-preserving (ADR-0144 §4a: characterisation). It edits the specs
themselves, though, so "covering tests pass unmodified" cannot apply
literally. The evidence is instead:

- the discovered test list (`pnpm test:e2e --list`) is identical before and
  after;
- the affected Playwright projects pass on the full stack before and after;
- **the ordered assertion inventory of every affected test is unchanged**,
  recorded by a reporter that captures Playwright's `expect` steps (title and
  subtitle, not location). The built-in JSON reporter cannot be used for this:
  it keeps only `test.step` entries and drops every `expect` step, as checked
  against the pinned `playwright@1.63.0`;
- for each extracted helper that asserts arrival, **one deliberate break**:
  the helper's arrival locator is altered once, a consuming test is observed
  red on that assertion, and the change is reverted. This catches the case the
  first three miss, a dropped `await` that turns the assertion into a
  floating promise. `eslint.config.mjs` carries no type-aware rules, so
  `no-floating-promises` is not on.

## Consequences

**Positive**

- The deferred extractions (spec 288 §1.5) have a rule to land against. The
  next wall spec imports the session rig instead of copying about 80 lines.
- One module per surface keeps ADR-0109 contention local. Two branches
  collide only if they touch the same surface's module, not a single shared
  fixtures file.
- Any spec can use a helper, including the 14 that drive their own contexts,
  because a helper receives the page instead of injecting it.
- The assertion-inventory check gives future e2e refactors a reusable
  characterisation mechanism. Before this ADR there was none.

**Negative / costs**

- **Nothing enforces the threshold.** A future spec can still re-type a
  locator. Review catches it, or not. That is accepted: a lint rule for
  "this locator exists in `support/`" would be speculative tooling
  (ADR-0036).
- **More files in `e2e/support/`.** They are all ADR-0109 contention files by
  that ADR's existing glob. Per-surface modules limit the blast radius but do
  not remove it.
- **Two styles will coexist.** Specs with no cross-file duplication keep
  inline locators indefinitely. That is the intended end state, not an
  unfinished migration.

## Alternatives Considered

### Class-based Page Objects (`new OverlaysPage(page)`)

Rejected for now. They would work with any page, but they pay off when a
surface has a large locator set used from many files, and none does here: the
one large per-surface set measured (`GEOMETRY_ERROR_TEST_IDS`) lives in one
file. Classes also make it natural to move assertions into methods, away from
the comments that explain which assertion is the red line. Consistency would
mean touching all 30 specs. A later ADR may promote a single surface to a
class if its locator set ever justifies it. This ADR does not pre-approve that.

### `test.extend` fixtures (exposing helpers or Page Objects)

Rejected. A fixture binds to the test-scoped `page`, which does not reach the
14 specs that drive their own contexts. It changes the `test` import in every
spec that uses one. It puts the shared surface in one module, which becomes
the most-contended file in `e2e/`, the same contention that caused the
duplication. And its main benefit, paired setup and teardown, is already
provided by the `seed`/`cleanup` projects. The fixture-era convenience that
usually comes with it, a signed-in `storageState`, is ruled out by ADR-0108.

### Rewrite every spec to one convention in one PR

Rejected. About 30 files churned in one branch conflicts with every in-flight
e2e branch (ADR-0109). A uniform style buys nothing where there is no
duplication.

### Leave it

Rejected. The specs themselves record the copying as waiting for "a separate
refactor". Leaving it means the fourth and fifth copies of the wall rig get
made the same way.

## Implementation Notes

- Spec 288 carries the first three extractions: US1, the wall session rig;
  US2, console navigation; US3, repeated form flows. Each is one PR.
- The assertion-inventory reporter and its comparison script land with the
  first of those PRs, under `scripts/`, with a `node --test` guard picked up
  by the existing `test:guards` glob.
