# Verification 469 — The domain model is written down, per bounded context, and the README tells the truth

Phase 5 (ADR-0037). Everything below was observed on branch `469-domain-model-docs` on 2026-10-31. The
one thing not observed is how GitHub renders the pages: that is checked on the PR (SC-003, last section).

## The guard (`scripts/domain-docs-trace.mjs`)

**Red first (T001).** `node --test scripts/domain-docs-trace.test.mjs` before the script existed:

```text
Error [ERR_MODULE_NOT_FOUND]: Cannot find module '…/scripts/domain-docs-trace.mjs' imported from …/domain-docs-trace.test.mjs
ℹ tests 1   ℹ pass 0   ℹ fail 1
```

**Green (T002).** 12 tests pass with the test file unmodified. Later additions (the overview's citations,
15 tests; the live-tree test, 16 tests) were each written red first; the 12 originals never changed.

**Counterfactuals (T003), one break at a time, each caught by exactly one test:**

| Break | Test that failed |
|---|---|
| stop excluding `obj/` and `bin/` | `ignores types under obj/ and bin/` |
| an `Omitted` entry needs no reason | `rejects an Omitted entry that gives no reason` |
| do not check a cited path exists | `rejects a cited path that does not exist` |

Two more, against the real tree:
- Broke two citations in `docs/domain/README.md` (a dead line `Program.cs:9999`, a dead path
  `CameraRegisteredSimHandlerX.cs`): 3 violations reported; restored, 0.
- Removed the backticks from the one mention of `Creation` on the SystemVariables page: the live-tree test
  went red with `Domain type "Creation" (context SystemVariables) is not named on docs/domain/system-variables.md:
  name it in backticks, or list it under "## Omitted" with a reason.`; restored, green. (A first attempt on
  `VariableValue` did **not** fail, because that name is also inside other backticked spans on the page. That
  was a flaw in the counterfactual, not in the guard.)

**Inventory (T004).** The guard's count equals the plan's first-pass grep in every context:

| Context | Domain types |
|---|---|
| AuditObservability | 22 |
| Automation | 27 |
| CameraCatalog | 13 |
| EventIngestion | 50 |
| Identity | 14 |
| LayoutComposition | 51 |
| OverlayDesigner | 24 |
| StreamDistribution | 21 |
| SystemVariables | 18 |
| **Total** | **240** (168 records, 30 record structs, 19 classes, 17 interfaces, 6 enums) |

Two blind spots were grepped and are empty: same-line attributes before a declaration, and declarations
indented 8+ columns.

**Whole repo (T019):** `node scripts/domain-docs-trace.mjs` → `0 violation(s) across 9 context(s).`
`pnpm test:guards` → 335 tests, 335 pass (the live-tree test is one of them).

## Review of the pages (T011)

Each page was produced by a read-only agent, then checked by me against the source: the guard, plus cited
lines read next to the claim they support. 40 citations were sampled across the nine pages
(EventIngestion 4, Automation 4, Identity 4, LayoutComposition 4, OverlayDesigner 4, SystemVariables 4,
AuditObservability 5, StreamDistribution 5, CameraCatalog 5) and every one matched. Beyond the sample:

- `RuleName` (Automation): the claim "2 to 63 chars, lowercase letters, digits or `-`, starts lowercase" is
  correct (`RuleName.cs:7-26`, grammar `^[a-z][a-z0-9-]{1,62}$`); the cited line is the start of the
  `Ensure` chain.
- `Variable.Type` is fixed after definition: private setter, assigned once at `Variable.cs:89`.
- `RtspUrl` enforces more than the page lists (user-info, at-sign, host, bracket and control-character rules);
  the page's list is a subset and is correct as far as it goes.
- AuditObservability's 26 `Handle` overloads equal the 26 `IIntegrationEvent` implementers in
  `Shared.Contracts` (counted independently: 26 and 26).

**Corrections made because of the review:**

| Where | What was wrong | Fix |
|---|---|---|
| `overlay-designer.md` Questions | recorded an "ADR-0104 vs code disagreement" about Published → Draft; the ADR lists *revert* among its own operations | rewritten: consistent, no disagreement claimed |
| `audit-observability.md` | idempotency claim cited the repository interface, not the index | now cites the unique index `ux_audit_event_identifier` (`AuditEventConfiguration.cs:162`) |
| `spec.md` / `tasks.md` / checklist | claimed ADR-0044's "9 contexts" disagreed with `src/` | decision 016 locks the same nine; consistent; the spec was corrected |
| `docs/domain/README.md` (my draft) | said ScenarioSimulator's sensor half "is a design, not code" | wrong: `Mqtt/MqttPublisher.cs`, `Cues/`, `Seeding/` exist; rewritten from the code |
| `docs/domain/README.md` (my draft) | said ApiGateway owns TLS | only CORS (`Program.cs:12`) and rate limiting (`:31`) are in code; TLS is in ADR-0106 only; stated as such |

**Open questions the agents could not settle from code** remain listed under **Questions** on each page
(for example where name uniqueness is enforced for cameras and variables, and how Wolverine discovers
`WebhookIntegrationRotatedV1Handler`). They are recorded, not asserted.

## The context map (T013)

26 integration events, each with exactly one publisher and a citation, generated from the nine pages
(not typed). Every event is also audited. Cross-context edges, all read from `Handle`/publish calls:
CameraCatalog → StreamDistribution, ScenarioSimulator; EventIngestion ↔ Identity (webhook rotated / revoked);
EventIngestion → Automation; Automation → SystemVariables, LayoutComposition;
OverlayDesigner → LayoutComposition, SystemVariables; SystemVariables → LayoutComposition.
`SystemVariableValueRequestedV1` is **published by Automation** (`FabEventIngestedV1Handler.cs:164`), though
its contract file lives under `Shared.Contracts/SystemVariables/`.

## Mermaid, links (T015, T019)

- 7 Mermaid blocks (overview 1, Automation 1, EventIngestion 2, LayoutComposition 1, OverlayDesigner 1,
  StreamDistribution 1) all parse with `mermaid@11`'s own parser run in a throwaway directory outside the
  repo; a deliberately broken block fails (`Parse error on line 3`), so the check is not vacuous.
- 32 relative links and in-page anchors across `docs/domain/*.md` and `README.md`: 0 dead.

## README (T016, T017)

17 claims were tabulated for Status / "What it does" / "Architecture at a glance" (full table kept in the PR
description). Result: 5 wrong, 2 partly wrong, 4 stale or unverifiable, 2 decision-only/unbuilt, 4 verified
(plus the claim "operators steer cells, switch cameras", which had no code evidence).

| Was | Is | Source |
|---|---|---|
| "specs 001-004 shipped" | status describes the nine contexts and four apps; `specs/` named as the record | `specs/`, `src/` |
| "frame-accurate sync via PTP" | aligned by receiver playout; not frame-matched; inter-display sync unbuilt | ADR-0128, ADR-0129 |
| "Marten for event-sourced contexts" | Marten permitted but unused | ADR-0130 |
| "Grafana stack" | one sink per environment | ADR-0118 |
| "Keycloak per fab" | one realm per deployment; fabs are groups | ADR-0159 |
| "RTSP / ONVIF", "control PTZ" | RTSP only; ONVIF and PTZ planned (0 hits in `src/` and `apps/`) | grep |
| "GPU transcode fallback" | a decision, not built (0 code hits) | grep, ADR-0000 |
| "k3s + Helm (prod)" | unbuilt target | ADR-0159, issue 1015 |

MQTT and webhook ingress, added to the README, were checked in code (`MosquittoConnectionFactory`, 25
EventIngestion files) before writing.

## Scope (SC-006, FR-011)

`git diff --stat origin/develop...HEAD`: 18 files, only `docs/domain/` (10), `README.md`,
`scripts/domain-docs-trace.mjs` and `.test.mjs`, `specs/469-domain-model-docs/` and
`.specify/feature.json`. Nothing under `src/`, `tests/`, `apps/`, `docs/adr/` or `.specify/memory/`.
`dotnet test tests/Architecture.Tests -c Release` on this branch: **3254 passed, 0 failed**. It was run
because `RetiredSeededPasswordTests` scans `README.md`, which this branch edits (that class alone: 1 passed).

## Not observed

- **GitHub rendering of the Mermaid blocks and tables.** Parsed locally, not viewed on GitHub; to be viewed
  once on the PR (SC-003).
- **The guard on Linux CI.** It uses `node:path` and normalises separators, but the live-tree test has only
  run on Windows so far; the first CI run is its Linux test.
- **Description accuracy beyond the sample.** The guard proves types are named and citations resolve; 40
  sampled citations out of 820 in `docs/domain/` is evidence, not proof, for the rest.
