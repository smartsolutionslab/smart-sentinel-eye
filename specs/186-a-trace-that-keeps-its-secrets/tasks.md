# Tasks 186 — A trace that keeps its secrets

**Spec:** `specs/186-a-trace-that-keeps-its-secrets/spec.md`
**Plan:** `specs/186-a-trace-that-keeps-its-secrets/plan.md`
**Issue:** [#2287](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2287)
— **already on Project #13** ("Smart Sentinel Eye"), status **Todo**, labels
`tech-debt`, `agent:ready`, no `agent:blocked`. Verified 2026-09-19 via
`gh issue view 2287 --json projectItems`. **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED**

Behaviour-changing, and not ambiguous — ambiguity would resolve to red anyway.
An artifact that today carries a bearer token, a refresh token, a client secret
and a realm password stops carrying them. A CI job gains a step and an upload
gains a precondition. None of that is behaviour-preserving, so characterisation
is the wrong colour.

### How "red" is demonstrated, given the subject is a binary artifact

This is the unusual part of this spec and it is settled here, not left to
phase 4.

**It is an ordinary automated red.** The defect lives in a zip, but a zip is a
file, and a file is something `node --test` can open. The guard
(`scripts/scrub-playwright-artifacts.test.mjs`) runs under the **existing**
`pnpm test:guards` (`package.json:15`) in CI's **`frontend`** job
(`ci.yml:171`) — Docker-free, browser-free, seconds not minutes. No manual step
is needed to observe the red.

**The red is asymmetric, and the asymmetry is the acceptance test for the red
itself.** Run before any scrubber exists:

- **The presence assertions (AS-2) must PASS.** Over the committed
  `leaky-trace.zip`, each of the four sentinels must be found, in the specific
  entries `spec.md` §1.3 names for it:
  - `SSE_FAKE_ACCESS_TOKEN_PAYLOAD` → `trace.network` **and** a
    `resources/*.json`
  - `SSE_FAKE_REFRESH_TOKEN_VALUE` → a `resources/*.json`
  - `SSE_FAKE_CLIENT_SECRET` → `trace.network`, `trace.trace` **and** a
    `resources/*.dat`
  - `SSE_FAKE_PASSWORD_VALUE` → `trace.trace`, in all three sub-places
    (the `fill` param, the `log` message, the DOM snapshot's
    `__playwright_value_`)
- **The absence assertions (AS-1, AS-3, AS-4, AS-5) must FAIL**, because
  `scripts/scrub-playwright-artifacts.mjs` does not exist. A module-not-found
  failure is an acceptable first red **only for the first run**; once the
  scrubber exists as a stub, the failure must be a sentinel that is still
  present, quoted by name.

**Three stop conditions, each reported rather than worked around:**

- **If a presence assertion fails, STOP and report.** The fixture is not
  actually leaky, or it is not the shape §1.3 measured. Every absence assertion
  downstream would then pass vacuously, and a scrubber validated against a
  fixture with nothing to scrub is theatre. This is the counterfactual that
  proves the guard can fail on its own subject.
- **If an absence assertion passes before the scrubber exists, STOP and
  report.** Something is already removing these values, which contradicts
  `spec.md` §1.3, and the premise needs re-checking before anything is changed.
- **If the guard reports it scrubbed zero files, STOP and report.** That is
  AS-7 reproduced inside its own fix — an assertion passing over an empty set.

**Phase 5 adds the half a fixture cannot give.** The fixture carries planted
sentinels; only the live stack carries real Keycloak tokens. `spec.md` §6.2 is
the counterfactual against real credentials — force a real e2e failure, grep
the real trace before scrubbing (must hit), scrub, grep again (must be zero),
then open it in `playwright show-trace`. **That is the step that converts the
issue's own "trace contents inferred" into "observed", and it is what the issue
literally asks for.** SC-007 requires both halves; neither is sufficient alone.

### The characterisation half, declared alongside the red

Green today and green after, with **assertions unmodified**:

- **`scripts/summarise-e2e-retries.test.mjs`** — the whole file. It drives
  `summarise-e2e-retries.mjs` against a report file, and §7.5 rewrites report
  files. If any of its assertions has to be edited, the redaction has broken
  the report's structure and SC-004 has failed. **Block, do not adjust.**
- **The rest of `pnpm test:guards`** — `wait-for-e2e-stack.test.mjs`,
  `settle-rule.test.mjs`, `lint-scope.test.mjs`, `test-collection.test.mjs`,
  `create-new-feature.test.mjs`, `retire-e2e-cameras-recovery.test.mjs`. A new
  sibling `.test.mjs` must not perturb them.
- **`pnpm lint:e2e` and `pnpm typecheck:e2e`** — `playwright.config.ts` is in
  scope for both, and for the second it is not obvious: `package.json:13` names
  the file directly, while `package.json:17` runs `tsc -p e2e/tsconfig.json`,
  whose `include` carries an explicit `"../playwright.config.ts"`. Checked, not
  assumed. A one-token config change must leave both clean.

No exemption is pre-declared. If an assertion needs editing, that is a finding.

### Phase 4 agents

- **4a — `test-writer`.** Writes, in order: the fixture generator, the fixture
  (by running it), the report fixture, and the guard. Runs the guard against
  the tree with **no scrubber present**, observes the asymmetric red above, and
  returns the **verbatim** output. That output is `infra-engineer`'s brief and
  is quoted in the PR body (ADR-0139).

  *Why the fixture is the test-writer's and not the engineer's:* it is the
  apparatus that makes the red observable — the same role spec 185's workflow
  reader and spec 184's probe corpus played. Handing it to the engineer would
  mean the engineer authoring the specification of its own fix, and here that is
  sharper than usual: whoever chooses the fixture's contents chooses which leaks
  the scrubber is ever asked to close.

  **May not write any part of `scrub-playwright-artifacts.mjs`.**

- **4b — `infra-engineer`.** Writes the scrubber, adds the devDependency, sets
  `sources: false` in `playwright.config.ts`, and adds the CI step plus the
  upload's `if:` gate.

  **May not edit the guard or the fixture to pass.** They are the
  specification. If the guard is wrong, that is a report, not an edit.

  *Why `infra-engineer` and not `frontend-engineer` — checked against this
  repository, not assumed.* The brief raised this as an open question because
  Playwright config sits near frontend testing in some repos. It does not here:

  - **`playwright.config.ts` lives at the repo root, deliberately.** ADR-0108:
    specs and config live *"**outside** the `apps/*` pnpm workspace, so they
    are not pulled into the per-app `lint`/`typecheck`/`test` runs"*. It is not
    an app file and no app build sees it.
  - **The diff's centre of gravity is a Node CLI script and a YAML workflow
    step.** No React, no TypeScript app code, no RTK Query, no component, no
    accessibility surface — nothing `frontend-engineer`'s brief covers.
  - **Git history agrees.** The closest precedent is spec 145 / #2077, which
    touched exactly this set — `playwright.config.ts` + `scripts/*.mjs` +
    `ci.yml` — and landed as `12211126 feat(ci): a retried e2e pass says so`.
    Commit scope `ci`, not `frontend`.

  One file is shared with 4a's world (`scripts/`), which is why 4b may not edit
  the guard; the directory is shared, the files are not.

### Phase 6 reviewers: **`security-reviewer` (primary) + `infra-reviewer`**

- **`security-reviewer` — REQUIRED, and this is not a formality.** The issue
  came from the whole-project security review and its asset is credentials.
  The discriminator this repository uses for workflow changes (does it touch
  `permissions:`, add or re-pin an action, introduce a secret, add
  `pull_request_target`, or widen what runs on untrusted input?) is the wrong
  test here — it would say no, and it would be answering a different question.
  The right question is whether the *data-handling* is correct. Specifically:

  1. **Does the scrubber actually remove every occurrence, or only the ones the
     guard looks for?** The guard can only assert the sentinels it planted. A
     reviewer reading `plan.md` §7.3-7.4 against a real unzipped trace is the
     only check on the surfaces nobody thought of.
  2. **`trace.network`'s three-places-per-record trap** (`spec.md` §1.3 S2) —
     `request.url`, `request.queryString[]`, and the header list. Confirm all
     three, not one.
  3. **The DOM snapshot's `__playwright_value_`** (S5) — the surface the issue
     never mentioned. Confirm it is handled and that the handling keys on the
     element being a password input, not on a hardcoded password list.
  4. **Does the scrubber's own source contain a secret?** `plan.md` §7.3 chose
     contextual redaction precisely so it would not have to hold
     `Operator1234`. Confirm no realm password was written into a second file
     in order to remove it from a first.
  5. **Does the committed fixture contain a real credential?** Every value in
     it must be a planted `SSE_FAKE_*` sentinel. This is a binary blob entering
     the repository; someone must have unzipped it.
  6. **The new devDependency** (`plan.md` §3) — exact pin, zero transitive
     deps, devDependency only, not imported from `src/` or `apps/`.
  7. **Fail-closed, verified not argued** — that SC-005 was demonstrated.
  8. **Over-redaction is the correct bias** — flag under-redaction as a defect
     and over-redaction as at most a note.

- **`infra-reviewer` (secondary).** Owns the `ci.yml` diff and the question its
  brief is written around — whether a green run proves anything:

  - Step **ordering**: the scrub sits after `Stop the AppHost` and before
    `Upload Playwright report`.
  - `if: always()` on the scrub, and `if: always() && steps.scrub.outcome ==
    'success'` on the upload. Confirm the *skipped* case fails the comparison
    too, so a scrub that never ran means an upload that never runs.
  - **No `continue-on-error: true` on the scrub step** — it would silently
    defeat the gate while leaving it looking present (`plan.md` §10).
  - That no job name, `needs:`, `timeout-minutes` or action pin moved —
    `AgentBriefClaimTests` asserts the briefs against those.
  - That the guard genuinely runs in the `frontend` job (`ci.yml:171` →
    `pnpm test` → `test:guards`), and is not accidentally scoped into the
    40-minute job only.

- **`frontend-reviewer`: no.** Checked against this diff rather than carried
  over. No app code, no component, no RTK Query, no OIDC wiring, no
  accessibility surface. `playwright.config.ts` is outside the `apps/*`
  workspace by ADR-0108, so none of the per-app gates it owns apply. The one
  line it might have claimed — `sources: false` — is a tracing knob, not a
  frontend concern. If review finds app code in the diff, this conclusion is
  void and `frontend-reviewer` must be added.

- **`backend-reviewer`: no.** There is no C# in the diff. No `.csproj`, no
  domain model, no value object, no handler, no migration. NetArchTest and
  `PrimitiveBoundaryTests` see nothing.

### Files this feature may touch

- `playwright.config.ts`
- `.github/workflows/ci.yml`
- `scripts/scrub-playwright-artifacts.mjs` *(new)*
- `scripts/scrub-playwright-artifacts.test.mjs` *(new)*
- `scripts/fixtures/trace-redaction/**` *(new)*
- `package.json`, `pnpm-lock.yaml` *(one devDependency)*
- `specs/186-a-trace-that-keeps-its-secrets/**`

### Files this feature may NOT touch

- **`src/**`** — no production assembly changes. `LayoutComposition/Api/Program.cs`
  is read to explain the `?access_token=` pattern; that pattern is the
  documented Microsoft SignalR approach and is **correct**. If a change to it is
  proposed, the scope has been misread.
- **`e2e/**`** — the literal realm passwords there are a real smell and are out
  of scope (`spec.md` §3). `sources: false` removes them from the *exported
  artifact*, which is this issue's concern. The one permitted exception is
  phase 5's temporary broken assertion (`spec.md` §6.2 step 2), which **must be
  reverted** and must not appear in the diff.
- **`scripts/summarise-e2e-retries.mjs`** and its test — characterisation
  subjects. If the report rewrite breaks them, that is a finding about §7.5,
  not a reason to edit them.
- **`apps/**`** — nothing here is app code, and the new dependency may not be
  imported there.
- **The `trace` mode and `video` mode in `playwright.config.ts`** — changing
  `on-first-retry` to `retain-on-failure` is a different intent and would widen
  the exposure (`spec.md` §2). Only `sources` changes.
- **`.github/workflows/*` other than `ci.yml`.**

### Latency budget

**N/A.** No production assembly changes; none of the six legs of `event
arrival → overlay rendered` is touched and no leg's measurement status moves
(`spec.md` header).

---

## Task list

`[ID] [P?] [Story]` — `[P]` marks tasks that own disjoint files and may run in
parallel (ADR-0109).

### Phase 4a — the red (`test-writer`)

- **T001 [US1]** Write `scripts/fixtures/trace-redaction/make-leaky-trace.mjs`:
  a standalone generator that boots a throwaway `node:http` server mimicking the
  four real shapes (`spec.md` §6.1 — Keycloak token response, bearer-
  authenticated API call, SignalR negotiate with `?access_token=`, Keycloak-
  shaped password form), drives headless Chromium under
  `tracing.start({ screenshots: true, snapshots: true, sources: true })`, and
  writes the zip. Every secret is an `SSE_FAKE_*` sentinel. Head comment states
  that it needs Chromium, is run **by hand on a Playwright version bump**, and
  is not run in CI.

- **T002 [US1]** Run T001; commit `leaky-trace.zip`. **Unzip it and confirm by
  eye that no real credential is present** — this is a binary entering the
  repository. Record the entry list and the byte size in the phase-4a report.

- **T003 [P] [US1]** Write `scripts/fixtures/trace-redaction/leaky-report.json`:
  a minimal Playwright JSON report with sentinels planted in `stdout`, `stderr`,
  an error `message` and an attachment `body` — the four fields
  `scripts/summarise-e2e-retries.test.mjs:17-19` already names. Disjoint from
  T001/T002; different file.

- **T004 [US1]** Write the **presence** half of
  `scripts/scrub-playwright-artifacts.test.mjs` (AS-2): unzip the fixture and
  assert each sentinel is in the entries §1.3 names for it, including all three
  sub-places for the password. **Run it — it must PASS.** Depends on T002.

- **T005 [US1]** Write the **absence** half (AS-1, AS-3, AS-4, AS-5): copy the
  fixtures to a temp dir, invoke `scripts/scrub-playwright-artifacts.mjs` as a
  real child process (the shape `summarise-e2e-retries.test.mjs` already uses),
  re-unzip, assert every sentinel absent; assert `request.url` **and**
  `request.queryString` both clean; assert an extension-less copy was scrubbed;
  assert the four report fields are clean and the file still parses. Depends on
  T003, T004.

- **T006 [US2]** Write the invariant half: AS-6 (a corrupt zip is **deleted**
  and the exit code is non-zero) and AS-7 (an empty directory exits non-zero).
  Build the corrupt case by truncating a copy of the real fixture. Depends on
  T004.

- **T007 [US1]** Write the structural-integrity assertion (SC-003): the
  scrubbed zip re-unzips, `trace.network` parses line-by-line as JSON, and the
  entry **names** are unchanged from the input. Depends on T005.

- **T008 [US1+US2]** Run the whole guard with no scrubber present. Confirm the
  asymmetry: T004 green, T005/T006/T007 red. **Return the verbatim output.**
  Apply the three stop conditions above. Depends on T004-T007.

### Phase 4b — the fix (`infra-engineer`)

Brief = T008's verbatim output.

- **T009 [US1]** Resolve and pin the zip dependency exactly (`plan.md` §3);
  add to the workspace-root `devDependencies`; `pnpm install`; commit the
  lockfile change. Confirm zero transitive dependencies.

- **T010 [US1]** Write `scripts/scrub-playwright-artifacts.mjs`: the walk
  (§7.2, **magic-bytes detection, not extension**), the structural pass (§7.3),
  the pattern backstop (§7.4, latin1 round trip), the JSON report path (§7.5),
  and fail-closed exit handling (§7.6). Depends on T009.

- **T011 [US1]** Run the guard. T005, T006, T007 go green; T004 stays green.
  **If T004 goes red, the scrubber has deleted or emptied the fixture rather
  than redacting it** — that is a defect, not a pass.

- **T012 [P] [US1]** `playwright.config.ts`: add `sources: false` to the trace
  option, changing `trace: 'on-first-retry'` to
  `trace: { mode: 'on-first-retry', sources: false }`. **`mode` stays
  `on-first-retry`** and `snapshots`/`screenshots` stay at their defaults
  (`spec.md` §2 rejects (A) and the mode switch). One-line comment giving the
  reason: the trace embeds the e2e sources, which carry literal realm
  passwords. Run `pnpm lint:e2e` and `pnpm typecheck:e2e`. Disjoint from
  T009-T011.

- **T013 [P] [US1]** `.github/workflows/ci.yml`: add the `Scrub credentials
  from the Playwright artifacts` step with `id: scrub` and `if: always()`
  between `Stop the AppHost` and `Upload Playwright report`; change the
  upload's condition to `if: always() && steps.scrub.outcome == 'success'`.
  Comment records the two independent guarantees (`plan.md` §8) and that
  `continue-on-error` would defeat the second. Disjoint from T009-T012.

- **T014 [US1]** Run the full characterisation set: `pnpm test:guards`,
  `pnpm lint:e2e`, `pnpm typecheck:e2e`. All green, no assertion edited.
  Depends on T011, T012, T013.

### Phase 5 — verify (`/verify`)

- **T015 [US1]** The live counterfactual, `spec.md` §6.2 in full: boot the
  stack, break one assertion in a signing-in spec, run with `CI=true` so
  `retries: 2` and `trace: 'on-first-retry'` both engage, grep the **real**
  trace before scrubbing (three counts, all non-zero), scrub, grep again (all
  zero), open it in `pnpm exec playwright show-trace`, re-check `apphost.log`
  for the negative control, **revert the broken assertion**. Depends on T014.

- **T016 [US1]** Demonstrate SC-005: make the scrubber fail deliberately and
  confirm no unscrubbed artifact reaches the upload — both guarantees, the
  script's own delete and the `outcome` gate. Depends on T014.

- **T017 [US1]** Confirm SC-008: a trace produced *after* T012 has no
  `resources/src@*.txt` entry, so the literal realm passwords are not exported.
  Depends on T015.

- **T018 [US1+US2]** Write `verification.md`. **Quote every figure** — the
  before counts, the after zeros, the entry lists, the exit codes. A
  measurement reported only to the orchestrator is invisible to every later
  grep and reviewer. Do not quote a whole token; a prefix and a count carry the
  finding. Depends on T015, T016, T017.

### Phase 6 — review

- **T019 [P]** `security-reviewer` — the eight items above.
- **T020 [P]** `infra-reviewer` — the `ci.yml` diff and the gate semantics.
- **T021** Address or accept in writing every finding. Depends on T019, T020.

### Phase 7 — PR

- **T022** `gh pr create --base develop` with the template. Body quotes T008's
  verbatim red, T015's before/after figures, and `Closes #2287`. Phase skips:
  none. Depends on T021.

---

## Dependency summary

```
T001 ──> T002 ──> T004 ──> T005 ──> T007 ──┐
T003 ─────────────┘  │                     │
                     └─> T006 ─────────────┤
                                           └─> T008  (the red, verbatim)
                                                 │
                     T009 ──> T010 ──> T011 ─────┤
                     T012 [P] ───────────────────┤
                     T013 [P] ───────────────────┤
                                                 └─> T014
                                                       │
                          T015, T016, T017 ────────────┤
                                                       └─> T018
                                                             │
                                       T019 [P], T020 [P] ───┴─> T021 ──> T022
```

**Foundational, blocking everything:** T001-T002. The fixture is the apparatus;
no red is observable without it, and no reviewer can check the scrubber against
a shape that was never captured.

**Genuinely parallel:** T003 with T001/T002 (different file); T012 and T013
with T009-T011 (`playwright.config.ts` and `ci.yml` are disjoint from
`scripts/` and from each other); T019 with T020 (read-only reviewers).

**Deliberately not parallel:** T004 before T005. The presence assertions must
be observed passing before any absence assertion is written, or the absence
assertions are unfalsifiable.
