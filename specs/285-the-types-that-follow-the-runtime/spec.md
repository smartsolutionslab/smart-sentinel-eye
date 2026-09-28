# Feature Specification: The types that follow the runtime

**Spec:** 285 (pre-reserved by the orchestrator; not auto-detected)
**Feature Branch**: `chore-2645-types-follow-the-runtime` (cut from `origin/develop` at `edf41b97`)
**Created**: 2026-09-28
**Status**: Draft — Phase 1, awaiting gate review
**Issue**: [#2645](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2645) — *Bump @types/node 22 -> 26 (tied to CI Node version)*. Deferred from spec 272 / PR #2640 (issue #2638). No labels; already on Project #13. **Lane:** supervised.
**Input**: "Bump @types/node 22 → 26 … needs a coordinated check of the CI workflow's Node version before bumping."

**ADRs and prior specs referenced:** ADR-0037 (phases), ADR-0074 (frontend stack: two Vite apps plus the root e2e workspace), ADR-0139 and constitution §Testing (new behaviour starts red), ADR-0087 (each commit builds on its own). Spec 088 §*Locked tech choices* (`@types/node@22`, "matching `engines.node` and the CI `setup-node` version"). Spec 272 §3.2 (the deferral reason: "The types must match the runtime floor in `engines`, not the newest Node").

**ADR gap, flagged, not filled.** No ADR records which Node major the repository runs on, or the rule that `@types/node` follows it. The rule exists only as a sentence in two merged specs (088, 272). This spec applies that rule; it does not change the runtime and so does not need to make the decision. Moving CI to a newer Node major **is** that decision, and belongs in its own issue with an ADR (see Out of scope).

---

## 1. What was measured (2026-09-28, tree `edf41b97`)

The issue asked for a check before bumping. This is the check. Every row came from a command that was run.

| What | Where | Found |
|---|---|---|
| CI runtime | `.github/workflows/ci.yml` — every `actions/setup-node` step | **3 steps** (lines 159, 637, 802 — frontend lint/typecheck/test, e2e shards, e2e shard-coverage check). All `node-version: '22'`. No other workflow file exists. |
| Declared runtime floor | root `package.json` `engines.node` | `^22.22.2 \|\| ^24.15.0 \|\| >=26.0.0` — lowest supported major **22** |
| Per-app runtime declaration | `apps/{kiosk-web,management-web,shared}/package.json` | none (no `engines`) |
| Version files | `.nvmrc`, `.node-version` | none |
| `@types/node` declaration | root `package.json` devDependencies only | `^22.20.4` — no app declares it |
| `@types/node` resolved | `pnpm-lock.yaml` | `22.20.4` (also the peer resolution under vite 8.3.1 and vitest 4.1.11) |
| Newest in-major release | `npm view @types/node@22 version` | **22.20.4** — already installed; nothing to bump inside 22 |
| Newest overall | `npm view @types/node dist-tags` | `latest` = 26.6.3; newest 24.x = 24.19.0 |
| Who consumes the types | `"types": ["node"]` in tsconfigs | **only** `e2e/tsconfig.json` (Playwright suite + `playwright.config.ts`). The app tsconfigs list `vite/client` and `vitest/globals` only. |
| Maintainer machine (context, not authority) | `node --version` | v24.21.0 |

**Conclusion.** CI runs Node 22 everywhere, and `engines.node`'s floor is 22. `@types/node` 22.x is therefore the correct major, and the repository is already on its newest release. **The bump to 26 is not warranted**: it would type-check Node 26 APIs in `e2e/` that the Node 22 runner does not have — a compile-green, run-red mismatch, which is exactly the risk the issue names.

Release-line context (Node.js release schedule, not re-verified by a run): Node 22 is in maintenance LTS until 2027-04-30; Node 24 is the active LTS; Node 26 is the *Current* line and not yet LTS at this date. That timeline is why a CI runtime move will be due within about seven months — and why it is a separate decision (Out of scope).

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 — A mismatched `@types/node` bump fails the frontend check (Priority: P1)

A maintainer (or a future dependency-freshness pass like spec 272) sees `pnpm outdated` report `@types/node 26.x` as `latest` and bumps it. Today nothing stops that: `typecheck:e2e` stays green, because newer typings are a superset, and the mismatch only surfaces when an e2e file calls a Node API the CI runtime lacks. With this story, the bump fails the existing frontend guard step with a message saying which Node major CI runs and that the typings follow it.

The rule is already written down twice (specs 088 and 272) and still produced an issue asking to break it. A rule a reviewer must remember is the shape this repository keeps having to correct; this story makes it a check.

**Why this priority**: It is the only change the investigation leaves. Without it, #2645 closes with "no change" and the next freshness pass re-files it.

**Independent Test**: On the feature branch, change the root `@types/node` range to `^26.6.3` and run the guard suite. It fails naming the mismatch. Restore; it passes. Separately, change one `setup-node` `node-version` in `ci.yml` to `'24'` and observe that it fails too (CI and the types disagree, and the three CI steps disagree with each other).

**Acceptance Scenarios**:

```gherkin
Scenario: the aligned state passes (happy path)
  Given every setup-node step in ci.yml declares Node 22
  And the root package.json declares @types/node in major 22
  And engines.node's lowest supported major is 22
  When the guard suite runs under the CI runtime
  Then the alignment check passes

Scenario: types bumped past the CI runtime (the conflict this issue is about)
  Given every setup-node step declares Node 22
  When @types/node is declared as ^26.6.3
  Then the alignment check fails
  And the failure names the declared @types/node major (26), the CI Node major (22), and the file to change

Scenario: CI steps disagree with each other
  Given two setup-node steps declare '22' and one declares '24'
  When the guard suite runs
  Then the alignment check fails and lists each step's declared version

Scenario: the running runtime is older than the typings
  Given @types/node is declared in major 22
  When the guard suite runs on a Node runtime whose major is below 22
  Then the alignment check fails naming the running version

Scenario: an unreadable declaration (bad input)
  Given ci.yml contains a setup-node step with no node-version, or a non-numeric one
  Or package.json declares no @types/node, or engines.node has no parsable major
  When the guard suite runs
  Then the check fails naming what it could not read, rather than passing vacuously
```

Auth scenarios: not applicable — no endpoint, no caller identity; this is a repository-level check.

### Edge Cases

- **A newer local runtime.** The maintainer machine runs Node 24. The typings are a *floor*, so a runtime above the types' major must pass; only a runtime below it fails.
- **Zero setup-node steps found.** A refactor that renames the action or moves the steps out of `ci.yml` must fail the check, not pass it with nothing compared.
- **An `engines` range whose alternatives are not in order.** The floor is the lowest major across all `||` alternatives, not the first one written.
- **A `node-version` written as `22.x`, `'22'`, `22` or `lts/*`.** Numeric forms reduce to their major; an alias such as `lts/*` cannot be compared and fails as unreadable (it would also make CI's runtime move silently, which is worth a failure).
- **A per-app `@types/node` added later.** Out of the check's reach unless declared at the root; recorded as an assumption, not guarded (no app declares it today).

---

## 3. Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `@types/node` stays on major 22. No version, lockfile, `engines` or `ci.yml` value changes in this feature.
- **FR-002** An automated check, run by the existing frontend CI step, fails when the major declared for `@types/node` differs from the Node major declared by the CI `setup-node` steps.
- **FR-003** The same check fails when the CI `setup-node` steps do not all declare the same Node major.
- **FR-004** The same check fails when the lowest major admitted by root `engines.node` differs from the CI Node major.
- **FR-005** The same check fails when the Node runtime executing it has a major below the declared `@types/node` major — the one assertion that asks the running system rather than a file.
- **FR-006** Every failure message names the values compared and the file(s) to change. A value the check cannot read, or zero `setup-node` steps found, is a failure, never a pass.
- **FR-007** Issue #2645 is closed by the PR with §1's measurements, stating that @types/node follows CI's Node major and that a runtime move is a separate decision.

### Key Entities

- **CI Node major** — the major declared by the `setup-node` steps in `ci.yml`; the runtime every frontend check and e2e shard actually runs on.
- **Runtime floor** — the lowest major admitted by root `engines.node`.
- **Types major** — the major of the root `@types/node` range.
- The invariant: *CI Node major = runtime floor = types major*, and *running runtime major ≥ types major*.

## 4. Success Criteria *(mandatory)*

- **SC-001** On the unchanged tree, the check passes in CI (Node 22) and on a Node 24 maintainer machine.
- **SC-002** Each of the four counterfactuals — types at 26; one CI step at 24; `engines` floor at 24; a CI step with an unreadable version — turns the check red, observed and quoted in the PR (ADR-0139; memory *prove a guard by counterfactual*).
- **SC-003** `pnpm typecheck`, `pnpm lint`, `pnpm test` and `pnpm format:check` pass; the PR changes no dependency version and no lockfile line.
- **SC-004** #2645 is closed after merge, confirmed by reading its state (memory: *a PR mention rarely auto-closes the issue*).

## 5. Decision D1 — enforce, or only record? (gate reviewer: confirm or strike)

The issue can be discharged three ways. The spec proceeds on **A**; the gate reviewer may choose otherwise, and each alternative is a strict subset or a separate issue, so choosing it does not invalidate the investigation.

| Option | What it does | Cost |
|---|---|---|
| **A — guard (default)** | FR-001–FR-007: one `node:test` guard file in `scripts/`, picked up by the existing `test:guards` glob, so no CI or script edit. | One more test that reads `ci.yml`'s shape (precedent: `scripts/lint-scope.test.mjs`). A deliberate runtime move must update three files and the guard names them. |
| B — record only | FR-001 + FR-007: close #2645 with §1, no code. | Zero now. Recurrence is caught only by a reviewer remembering specs 088/272 — which is how #2645 came to be filed. |
| C — move the runtime | CI → Node 24, `engines` floor → 24, `@types/node` → 24.x. | Needs an ADR (runtime policy), a full e2e run on the new runtime, and is not what #2645 asks. Recommended as its own issue before Node 22 leaves maintenance (2027-04-30). |

## 6. Latency budget impact

**N/A.** No leg of constitution §IV is touched: the change is a repository check that runs in CI, not on the event-to-overlay path, and no shipped code or typing changes.

## 7. Assumptions

- **A1** The root `package.json` is the only place `@types/node` is declared; the apps inherit it through the workspace. True today (§1); a per-app declaration added later is not guarded.
- **A2** `ci.yml` remains the only workflow that installs Node. A new workflow with its own `setup-node` would not be compared unless the check scans every workflow file — the plan decides the scan scope.
- **A3** Release-line dates in §1 are quoted from the Node.js release schedule and were not re-verified by a command.
- **A4** Decision D1 = A until the gate reviewer says otherwise.

## Out of scope

- Moving CI, `engines` or the typings to Node 24 or 26 (Option C). Needs its own issue and an ADR for the runtime policy.
- Tidying the `engines.node` range (it admits the non-LTS `>=26.0.0`).
- Adding Dependabot/Renovate or an ignore rule so `pnpm outdated` stops listing `@types/node` — spec 272 already flagged the missing dependency-policy ADR.
