# Tasks 445: A webhook credential is audited when it is issued

[spec](spec.md) · [plan](plan.md) · Issue #3110 (feature-level; on Project #13 — verify by `content.url`)

**Phase 4a colour: RED** (new behaviour). **Security-sensitive: yes.** Engineer: backend-engineer only.
No foundational Kernel/AppHost task. `Shared.Contracts` gains one record, written as scaffold in T001 so every commit
builds; it blocks everything after it. Within 4b, T010-T012 (EventIngestion) and T013 (AuditObservability) own
disjoint files and run `[P]`.

**Forbidden:** `ReplayRegisteredClientQueryHandler` and anything in Identity (#3096); the revoke handler's null actor;
the HTTP contract of `POST /webhook-integrations`; any migration; ADRs, the constitution, CLAUDE.md; suppressions,
thresholds, analyzer configs; any existing assertion not named in plan §Phase 4a.

## Phase 4a — red (test-writer; production limited to the scaffold in plan §Phase 4a)

- [ ] T001 [US1] Write R1 and R3 against unchanged code; **quote the compile failure**. Then add the scaffold
      (contract record, widened domain event, `Register`/command/endpoint signatures, empty unregistered handler + log
      stub, call-site updates in the 9 test files). Solution builds.
- [ ] T002 [US1] Build with the contract present; **observe F1 and F2 red** (`BoundaryTests`), quote.
- [ ] T003 [US1] R2 handler tests (new file). Observe red (stub publishes nothing), quote.
- [ ] T004 [US1] R4 durable-list entry; R5 registration line. Observe red, quote.
- [ ] T005 [US1] R6 mapping case 9b. Observe red, quote.
- [ ] T006 [US1] R7 `VerbatimInventory` entries (F3 goes green — name it in the PR); R8 contract tests incl. shape pin
      (declared characterisation of the wire shape).
- [ ] T007 [US1] R9 integration test (new class) **plus its `ci-shards/shard-N.filter` entry**. Compiles locally;
      the red is the CI run of this commit, quoted from the job log (download it before any re-run).
- [ ] T008 Commit the red set (`test(3110): ...`), each commit building on its own.

## Phase 4b — implementation (backend-engineer; may not edit any test)

- [ ] T010 [P] [US1] `WebhookIntegrationRegisteredDomainEventHandler` publishes per plan §Messaging; `Log` message. R2 green.
- [ ] T011 [P] [US1] `EventIngestionInfrastructureModule`: register the handler, add the type to
      `durableOutboxMessageTypes`. R4, R5 green.
- [ ] T012 [P] [US1] Comments: `WebhookIntegration.Register` summary, `EventTypeRegisteredDomainEvent` summary.
- [ ] T013 [P] [US1] AuditObservability: `Handle(WebhookIntegrationRegisteredV1)` arm; `V1ResourceMap.Conventions`
      `Add<...>` + comment; `ResourceKind.cs:37-47` comment. F1, F2, R6 green.
- [ ] T014 Counterfactuals 1-5 (plan), each failure quoted, each reverted.
- [ ] T015 `dotnet build -c Release` clean; `dotnet format --verify-no-changes` on touched files; coverage gates
      (EventIngestion/AuditObservability Domain ≥ 90, Application ≥ 80, Shared ≥ 90).

## Phase 5 — verify

- [ ] T016 Verification note: red→green per R/F id; R9 green in CI with the row's fields quoted from the log or a
      local run if the stack is free; the dispatch-before-commit path read. Latency: N/A.

## Phase 6 — review

- [ ] T017 backend-reviewer.
- [ ] T018 **security-reviewer** (mandatory): no token or hash in either event, the durable envelope, the audit row or
      the log line; the actor is the authenticated subject, not request input; refusals publish nothing; the envelope
      commits before the token is written.

## Phase 7

- [ ] T019 PR to `develop` (`--base develop`), body quotes the reds, names the 18th ADR-0185 durable type for a human,
      and `Closes #3110`.
