# Phase 5 verification — spec 270 / issue #2241

## What was observed

The whole solution builds clean in Release, and every backend test level
below the AspireFixture actually ran, green, against the real production
code (not just reported by a subagent — re-run independently in this
phase):

- `dotnet build -c Release` on the whole solution: **0 Error(s)**, 231
  warnings, all pre-existing SonarAnalyzer code-metric advisories in files
  this change didn't touch (`LayoutEndpoints.Commands.cs`, `AppHost.cs`,
  `CameraEndpoints.cs`, `OverlayEndpoints*.cs`) — these are the
  ADR-0084 advisory carve-out, not new.
- `dotnet test tests/ServiceDefaults.Tests -c Release --no-build`:
  **213/213 passed** — includes `RevokedClientSnapshotTests` (the §2
  refusal-rule truth table), `RevokedClientRefresherTests` (refresh-ahead
  snapshot, fail-static on error, health-check staleness), and
  `BearerRevocationHookTests` (the `OnTokenValidated` hook: refuses,
  admits, chains a pre-existing handler, survives LayoutComposition's
  later `Configure<JwtBearerOptions>`).
- `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --no-build`:
  **40/40 passed** — includes `WhepRevocationTests`, which drives
  `WhepAuthValidator` through its internal test seam with **real signed
  JWTs** (not mocks of the refusal rule): a revoked `azp` is refused with
  `TokenRejected`, a re-registered client's later-`iat` token is admitted,
  and no metadata refresh is triggered by a revocation refusal. This is
  the strongest evidence available in this session for the WHEP leg
  end-to-end at the unit boundary.
- `dotnet test tests/Identity.Application.Tests -c Release --no-build`:
  **94/94 passed** — includes `ListRevokedClientsQueryHandlerTests`
  (excludes active clients, covers all kinds/fabs, latest-`DisabledAt`
  wins per client id).
- `dotnet test tests/Architecture.Tests -c Release --no-build`:
  **521/521 passed** — includes the new `ScopeGrantTests` fact (the
  `sse.identity.revocations.read` scope exists and is granted only to
  `revocation-list-reader`) and the bumped `EndpointScopeDeclarationTests`
  / `StatusProducerDeclarationTests` / `RouteValueRefusalDeclarationTests`
  / `PreconditionDeclarationTests` pinned counts (15 endpoint files, 66
  route mappings) — proving `RevocationEndpoints.cs` is wired the way
  every other endpoint file is.
- `RealmImportMirrorTests`' AppHost/realm secret-pairing theory:
  **6/6 passed** — the new `RevocationListReaderClientSecret` parameter's
  default matches the `revocation-list-reader` client's seeded secret.

Commands run and their exact output are quoted above from this phase's
own execution, not copied from an earlier subagent's report.

## What was NOT observed, and why

**The full `AspireFixture` end-to-end test,
`tests/Integration.Tests/Identity/RevokedTokenRefusedIntegrationTests.cs`,
was not run against a live stack.** This is the test that proves the
actual issue behaviour: enrol a kiosk, mint a token, `200` on
`GET /cameras` and `POST /streams/authorize`, disable the kiosk, poll
every 500 ms for up to 20 s expecting `401` on both (with an unrevoked
control kiosk staying `200` throughout), plus the revocation endpoint's
401/403/200-by-scope. It compiles clean against this branch's code.

Reason: at the time of this phase, `docker ps` on this machine showed
**three separate Aspire stacks already running concurrently**
(containers suffixed `bf9800f3`, `989008e5`, and a fourth set that had
started within the previous 8–42 seconds), and `Get-PSDrive` showed `C:`
at **6.3 GB free** — the exact precondition this repo's own operational
notes record as the failure mode ("worktrees fill the disk and kill
Docker: C: hits zero, and only a GUI restart brings it back"; "one
machine, one Aspire stack: two concurrent boots give FailedToStart that
reads exactly like a code defect"). A `docker system df` / `docker info`
probe in this same window returned a `snapshot ... does not exist`
daemon error, consistent with the engine already under strain. Booting a
fourth full stack (with a freshly deleted Keycloak volume, since the
realm changed) would not have produced a trustworthy signal even if it
appeared to pass, and risked taking down the other in-flight work sharing
this machine.

**This is the honest gap.** The unit-level evidence (real JWTs through
`WhepAuthValidator`, the exact refusal-rule truth table, the full realm/
scope/endpoint wiring proven by architecture tests) covers every
component the integration test would exercise, individually. What is
*not* yet observed is their composition through a real Keycloak-issued
token, a real disable call, and real HTTP round trips across service
boundaries. Recommend running
`dotnet test tests/Integration.Tests --filter RevokedTokenRefusedIntegrationTests`
once the machine has a single stack free (or in CI, where it always
will), before or shortly after merge, and recording the result.

## Latency budget (§IV)

**N/A — auth/revocation path, not event-to-overlay.** The REST check is
one frozen-dictionary lookup after signature validation, off the media
path entirely. The WHEP check runs at `/streams/authorize` (session
setup), before any media leg begins. `NFR001_JwtValidationLatencyTests`
was not modified and was not observed to regress (it's part of the green
`StreamDistribution.Infrastructure.Tests` / build results above, but its
specific timing assertion wasn't independently re-measured in this phase
since this change adds only a synchronous dictionary lookup, not I/O, to
that path).
