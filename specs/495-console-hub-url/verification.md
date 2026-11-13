# Verification: The operator console's live updates reach the hub

Observed, in the order the tasks ran. Nothing here is inferred.

## T001 — baseline (2026-11-13, branch `495-console-hub-url`, clean tree, **Release**)

| Suite                                                                                                                                  | Result                        |
| -------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| `tests/Integration.Tests`: `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostCompositionCharacterisationTests`, `LayoutHubCorsTests` | **31 passed**, 0 failed       |
| `tests/Architecture.Tests`: `BoundaryTests` (includes `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`)                  | **29 passed**, 0 failed       |

Two things that looked like failures and are not:

- **Run the composition snapshots in `Release`.** A Debug run fails all four lanes at `arg[3] "Release"` against `"Debug"` (the migrations
  rebuilder's configuration argument is part of the pinned composition). Not a defect on `develop`; the snapshot is a Release literal.
- **The full `Architecture.Tests` run does not finish locally.** `AgentBriefClaimTests.Every_brief_in_the_repository_is_scanned_by_this_guard`
  fails with 39 `unscanned` briefs, all under `.claude/worktrees/494-track-frontend/…`, and the test host then crashes. The guard walks the
  filesystem under the repo root and meets the nested worktree. CI has no worktrees, so this is a local-environment artefact. The other
  `AgentBriefClaimTests` pass in isolation. It clears when the `494-track-frontend` worktree is resolved.
