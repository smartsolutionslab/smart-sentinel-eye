# Tasks — Spec 336 (#2814)

Phase 3 of ADR-0037. Feature-level issue (#2814) on Project #13 — no
per-task issues (CLAUDE.md, post-spec-028 convention).

- [x] T001 — `IWebhookIntegrationRepository.IsRevokedByKeycloakClientIdAsync` +
      `WebhookIntegrationRepository` implementation — fail-closed `AnyAsync`
      over every row sharing the client id, not a `RotatedAt`-ranked pick
      (review finding; see plan.md §3).
- [x] T002 — Red: `ManualIngestRefusesRevokedWebhookCallerIntegrationTests`
      proves a revoked webhook caller's token is accepted before revoke,
      refused after, through `/events/manual`. Red observed live — see the
      PR body for the quoted failure.
- [x] T003 — `EventsEndpoints.Writes.IngestManual` calls the new check
      before fab resolution; `IngestManualServices` gains the repository.
- [x] T004 — Detach the revoked row from `management-web` (not reseed a
      fresher one) in the new test, and apply the same `finally`-wrapped
      cleanup to `WebhookRevocationRefusesDeliveryIntegrationTests`'s
      existing revoke test (plan.md §3; review finding).
- [x] T005 — `dotnet build -c Release` clean on `EventIngestion.Api`,
      `EventIngestion.Infrastructure`, and `Integration.Tests`.
- [x] T006 — Live verification: real red observed (see PR body), both
      classes added to `tests/Integration.Tests/ci-shards/shard-1.filter`.
