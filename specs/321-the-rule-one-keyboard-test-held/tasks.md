# Tasks 321: The rule one keyboard test held

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2374

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2374 is the tracking issue — no new feature issue.
- **Engineers:** `test-writer` (4a: the guard); `frontend-engineer` (4b: the ESLint block). No backend,
  no infra.
- **New ADR:** **no** — ADR-0169 already decides the rule and names this enforcement; the ESLint-rule
  form follows ADR-0150's precedent (plan §2).
- **Phase 4a colour: RED** — a new enforced gate (spec §7).
- **Latency:** N/A (spec §9).
- **Shard filters:** none — no new Integration.Tests class.
- **Spec number:** 321. 320 is claimed on `origin/fix/2181-orphaned-keycloak-client-on-aborted-register`.

Format: `[ID] [P?] [Story]`. Strictly sequential — T020 depends on T010's red, so no `[P]`.

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Prerequisite (orchestrator)

- [ ] **T001** Re-check spec number 321 across remote branches and open PRs before opening the PR.

## US1 (P1) — A composite that imports the store fails the build

- [ ] **T010** [US1] (test-writer) Create `scripts/composite-store-rule.test.mjs` per plan §4 (must-flag,
  must-not-flag, wiring, no-escape-hatch). Run `node --test scripts/composite-store-rule.test.mjs` against
  the unchanged config; return verbatim output. **Expected red:** every must-flag case and the wiring
  test. Green on must-not-flag / no-escape-hatch is expected and not evidence.
- [ ] **T020** [US1] (frontend-engineer, depends T010) Add the ADR-0169 block to
  `apps/shared/eslint.config.js` per plan §3. May not edit the guard. Done when: the guard is fully green,
  `pnpm --filter @smart-sentinel-eye/shared lint` exits 0 with no composite edited, and
  `prettier --check` passes on both files.
- [ ] **T030** [US1] (verify, depends T020) Spec §8 steps 3–4: counterfactual import in
  `composites/Badge.tsx` → lint red with the ADR-0169 message (quote it in the PR); same import in
  `api/cameras.api.ts` → lint green; revert both. Run full `pnpm test:guards`.

## Follow-up (orchestrator, not blocking the PR)

- [ ] **T040** File an issue — **without `agent:ready`**, it needs a human decision — recording spec §5's
  gap: `BackdropControls.tsx`, `CameraViewer.tsx` and `FrameGrabber.tsx` call RTK Query hooks from
  `shared/api` and so need a `<Provider>`, contrary to ADR-0169's decision, while passing its named
  mechanism; ADR-0169's "a new composite that reaches for a query hook fails the build" is therefore
  unmet. Options for the human: amend ADR-0169 to permit composites calling shared `api/` hooks, or widen
  the rule and refactor the three (behaviour-preserving, characterisation-first). Also note ADR-0169's
  `Status: Proposed`. Add to Project #13; reference the number in the T020 rule comment and the PR body.
