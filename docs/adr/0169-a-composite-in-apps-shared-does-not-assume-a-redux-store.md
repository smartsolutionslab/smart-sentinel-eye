# ADR-0169: A composite in `apps/shared` does not assume a Redux store

## Status

Accepted. Merged via PR #2791 (2026-10-08).

## Context

`apps/shared/src/ui/composites/**` holds components used by both `management-web` and `kiosk-web`. Several of them render in test suites that mount no `<Provider>`, including two characterisation baselines whose whole value is that they pin behaviour without a surrounding application.

The rule that a composite must not reach into the store **is already enforced** — by a single test, in a file about something else:

```ts
// apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx:693
it('Renders with no Provider in the tree, exactly as the three existing guards do', () => {
```

And it has decided real design questions at least three times:

- **Spec 148 (#2341)** put its `useResolveOverlayTextQuery` call in `OverlayEditorDialog` rather than in `OverlayEditor`, passing results down as props.
- **Spec 154 (#2347)** put its undo history in a plain React hook rather than Redux.
- **Spec 156 (#2372)** built `ChainRecoveryNotice` with `react` as its only import.

Its only written form is a functional requirement in **spec 150**, which sits on an unmerged branch. Nothing on `develop` states it.

This is the same shape as the constraints that do have ADRs — no cross-context project references (NetArchTest), the primitive ban in constitution §II (`PrimitiveBoundaryTests`), the `Ensure.That` guard convention (ADR-0105). Each is a constraint on where code may live, enforced mechanically, and written where someone can find it.

CLAUDE.md's own record is that undocumented rules drift: §II was summarised three different ways while 35 primitive properties accumulated; the Phase 3 board gate was documented for sixteen specs that ignored it; §IV recorded a leg as unbuilt after it was built. Today this rule survives because one keyboard-handling test happens to name it. A refactor that rewrites that test removes the enforcement **and** the only statement of the rule in one move, with nothing failing.

## Decision

**A component under `apps/shared/src/ui/composites/**` must render without a Redux `<Provider>` in the tree.** It receives data and callbacks as props; the query or dispatch lives in the feature-level container that renders it.

Scope is deliberately narrow:

- **`apps/shared/src/api/**` is unaffected.** It defines RTK Query endpoints and is expected to. The rule is about *components*.
- **`apps/shared/src/ui/primitives/**` is likewise unaffected**, and trivially so — primitives take props by construction.
- Feature-level containers in either app may use the store freely. That is where the seam is.

**Enforced, not advisory**, per ADR-0036's enforce-rules-advise-preferences split: an architecture test asserting that no file under `ui/composites/**` imports `react-redux` or `@reduxjs/toolkit`. This makes the rule mechanical rather than incidental to one test's survival.

## Consequences

- A composite that genuinely needs server state gets it as props, which makes its test surface smaller and its reuse across the two apps real rather than nominal.
- The existing `OverlayEditorKeyboard.test.tsx:693` test becomes redundant as *enforcement* but is worth keeping as documentation of intent at the point of use.
- A new composite that reaches for a query hook fails the build with a named reason rather than passing review by accident.
- **Cost, stated plainly:** prop-drilling. Spec 148 paid it — `OverlayEditorDialog` holds the resolve query and passes three props down. That is the trade the rule makes, and it is the right one only while composites stay shallow. If a composite tree ever grows deep enough that drilling becomes the dominant cost, this ADR should be revisited rather than worked around.

## Alternatives Considered

- **Advisory only.** Rejected: that is the status quo, and the status quo is one test in an unrelated file plus a requirement on an unmerged branch.
- **Ban the store across all of `apps/shared`.** Rejected: `apps/shared/src/api/**` exists precisely to define shared endpoints.
- **Allow composites to use the store, and drop the no-`Provider` tests.** Coherent, and would remove the drilling cost — but it would make every composite's test require a store, and would couple `kiosk-web`'s bundle to management state it does not use. Rejected on both grounds.
