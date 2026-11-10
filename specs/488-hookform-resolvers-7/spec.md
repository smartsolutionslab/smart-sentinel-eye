# Spec 488 — Bump `@hookform/resolvers` 6.0.0 → 7.0.5

**Feature Branch**: `488-hookform-resolvers-7`
**Created**: 2026-11-10
**Status**: Draft
**Issue:** #3239 (feature-level).
**ADRs:** ADR-0079 (React Hook Form + Zod), ADR-0036 (smallest change), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases).
**No new ADR.** A version bump inside an already-decided stack; nothing here makes or amends a decision.
**Latency budget (constitution §IV): NOT on the path.** The seven forms are operator forms in `management-web` and `management-cameras`;
no leg between event arrival and overlay render is touched. The PR says so.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are captured passing on the unmodified tree first,
and must pass **unmodified** after the bump. An assertion that has to be edited is evidence the behaviour moved: block, do not adjust.

## Problem

`@hookform/resolvers` is pinned at `6.0.0` in `apps/management-web` and `apps/management-cameras`; `7.0.5` is `latest`. It is the last
frontend dependency in these two apps that is a major behind, apart from `jsdom` (#3240) and `@microsoft/signalr`, which is
**out of scope by owner instruction (2026-11-10)**.

**Premise, checked against the release notes and the tree (2026-11-10).**

- The only breaking change in `7.0.0` is the **typebox** resolver (now peers on `typebox` ^1.0.0 instead of `@sinclair/typebox`; error
  `type` values and messages change; `TypeCompiler.Compile` is replaced). This repo imports **only** `@hookform/resolvers/zod`: seven
  import sites, seven `zodResolver(<schema>)` calls, no typebox anywhere.
- `7.0.1`–`7.0.5` fix fluentvalidation-ts, io-ts, effect-ts, valibot and ajv. None is the zod resolver.
- Peers of `7.0.5` are satisfied as pinned: `zod ^3.25.0 || ^4.0.0` (we have `4.6.7`), `react-hook-form ^7.55.0` (we have `7.91.0`),
  `@standard-schema/spec ^1.0.0` (already in the lockfile).
- The one peer that changed shape is `effect` (`^3.10.3` → `^4.0.0`); we do not use it.

The expected diff is therefore the two manifests and the lockfile, with no source change. The spec records that as the expectation, not a
guarantee: the proof is the suites, below.

## Scope

**In:** `apps/management-web/package.json`, `apps/management-cameras/package.json`, `pnpm-lock.yaml`.

**Out (explicitly):** any source or test file; `@microsoft/signalr`; `jsdom` (#3240); `react-hook-form`, `zod` and every other
dependency, including the patch and minor drift Dependabot owns; the three other apps, which do not depend on the package.

## User Scenarios & Testing

### User Story 1 (P1) — operators' forms behave exactly as before

```gherkin
Scenario: a form validates a bad value
  Given any of the seven zod-validated forms (register / rename / re-address a camera, rule, system variable, wall, overlay draft)
  When the operator submits an invalid value
  Then the same field shows the same error, and nothing is sent

Scenario: a form submits a good value
  Given the same forms with valid input
  When the operator submits
  Then the same request is sent as before the bump
```

### User Story 2 (P1) — the bump is proved, not assumed

```gherkin
Scenario: baseline first
  Given the unmodified tree
  When the management-web and management-cameras suites run
  Then the passing counts are recorded

Scenario: after the bump
  Given only the manifests and lockfile changed
  When the same suites run
  Then the counts are identical, with no test, assertion or snapshot edited
```

## Edge Cases

- A form whose resolver feeds `react-hook-form` a Standard-Schema-shaped result rather than a zod-shaped one: covered only if a test
  submits an invalid value; the characterisation step checks each of the seven forms has such a test, and **records any that does not**
  rather than adding one in this change (a gap is its own issue, per the phase-4a colour).
- The lockfile resolving a second copy of `@hookform/resolvers` or `zod` in one app: checked in the diff.
- `useOverlayDraftFormState.ts` has no sibling test; it is exercised through `OverlayDraftForm.test.tsx`. The characterisation step
  confirms that file reaches the resolver.
- A type error that `skipLibCheck: true` would hide in the package's own `.d.ts`: `tsc --noEmit` still checks our call sites, which is
  the surface that matters.

## Requirements

- **FR-001** Only `apps/management-web/package.json`, `apps/management-cameras/package.json` and `pnpm-lock.yaml` change. If a source or
  test file has to change, stop and report: that is evidence the premise was wrong, not a licence to adjust.
- **FR-002** Both apps pin `@hookform/resolvers` at the same exact version, matching the existing exact-pin convention.
- **FR-003** Baseline: the full vitest suites of `management-web` and `management-cameras` pass on the unmodified tree, counts recorded
  before the bump (ADR-0139 characterisation: observed green first).
- **FR-004** After the bump, the same suites pass with **identical counts** and no test file in the diff.
- **FR-005** `eslint --max-warnings 0`, `tsc --noEmit` and `prettier --check` are clean in both apps.
- **FR-006** The lockfile diff touches `@hookform/resolvers` only: no unrelated package moves, and no duplicate copy of `zod` or
  `react-hook-form` appears.
- **FR-007** Each of the seven `zodResolver` call sites is shown reached by at least one test that submits an invalid value, or the gap
  is recorded in `verification.md` and filed, not fixed here.
- **FR-008** The PR states that no §IV leg is touched, and that `@microsoft/signalr` was deliberately left alone.

## Success Criteria

- **SC-001** Test counts for both apps are identical before and after, with zero test files in the diff.
- **SC-002** The diff against `origin/develop` is the two manifests and `pnpm-lock.yaml`.
- **SC-003** All seven forms are accounted for in `verification.md`: covered by a named test, or recorded as a gap with an issue number.

## Assumptions

- `pnpm-lock.yaml` at the repo root is the lockfile of record; the package manager is pnpm (an earlier `npm outdated` in this session read
  a stale install, which is why versions are taken from the manifests).
- The release notes are accurate about what changed in `7.0.x`; the suites, not the notes, are the evidence.

## Dependencies

Independent of #3240 (jsdom) and of every open spec. Touches the same two manifests as #3240 would; the two PRs are unlikely to conflict
on lines (different keys) but both rewrite `pnpm-lock.yaml`, so the second to merge rebases.
