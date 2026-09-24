# Tasks 232 — The bucket one wall empties

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2221 (feature-level issue; no per-task issues)
**Engineer**: `infra-engineer` · **Phase 4a colour**: **RED** for every test task below.

Format: `[ID] [P?] [Story] description — file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). No foundational task in the
Shared.Kernel/Contracts sense; the **ordering constraint is evidential**: US2 may not start until
T002 shows a 429 (plan §5).

## US1 — the refusal names the submit's status

- [ ] **T001** [P] [US1] Record each iteration's submit status (and `requestfailed` text) keyed by value; append `submit answered <status>` / `submit failed: …` / `submit response unseen` to the "never painted" refusal. No assertion, budget or print-format change — `e2e/kiosk-shows-a-label-over-video.spec.ts`
- [ ] **T002** [US1] **Stop gate.** On the unfixed stack (local, needs a free stack slot — else CI fallback, plan §5): run `pnpm exec playwright test --project=kiosk --project=wall` at the default worker count, then `--project=kiosk` alone. Quote both refusals verbatim. **429 → continue. Anything else → `agent:blocked` with the output; stop.** Depends on T001.

## US2 — the dev/CI stack's gateway budget covers its screens

- [ ] **T003** [P] [US2] New model test: run mode → `api-gateway` env `RateLimiting__PermitLimit == "6000"`; `E2ETests=true` → key absent. Run it on the unchanged AppHost; capture the verbatim **red** — `tests/Integration.Tests/AppHostGatewayRateBudgetTests.cs`. Depends on T002.
- [ ] **T004** [US2] Add the gated `WithEnvironment("RateLimiting__PermitLimit", "6000")` with the plan §2 derivation and the production issue's number in the comment — `src/AppHost/AppHost.cs`. Depends on T003 (tests may not be edited to pass).
- [ ] **T005** [US2] Green, unmodified: T003's test; `AppHostReplicaCountTests`, `AppHostE2ESwitchTests`, `AppHostParameterOverrideTests`; `GatewayRateLimitIntegrationTests` (fixture still at 100/min); `pnpm lint:e2e`, `pnpm typecheck:e2e`, `format:check`; Release build clean. Depends on T004.

## Phase 5

- [ ] **T006** [US1+US2] Restart the AppHost; run the pair at the default worker count **twice**, `--project=kiosk` alone once, `--workers=1` once — all green, span test unmodified since T001. On the PR's CI, the `kiosk-*` shard log shows `[span] iteration 0:` exactly once and no `[span] REFUSED`. Depends on T005.

## Dependencies

```
T001 → T002 (stop gate) → T003 → T004 → T005 → T006
```

T001 and T003 own disjoint files and *could* be written in parallel, but T003 is only worth
writing if T002 confirms 429, so the chain is serial by evidence, not by files. One
`infra-engineer` does the whole slice.

## Characterisation — must stay green and unmodified

- Every `e2e/*.spec.ts` other than the one T001 edits; `playwright.config.ts` untouched.
- In `kiosk-shows-a-label-over-video.spec.ts`: every `expect`, `ITERATIONS`, budget constant and
  the `writeRenderLegRecord` payload, byte-for-byte outside the refusal message and its listener.
- `src/ApiGateway/appsettings.json` and `Program.cs`: not edited.
