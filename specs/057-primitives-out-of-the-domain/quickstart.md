# Quickstart: verifying feature 057

**Feature**: 057 | **Date**: 2026-09-02

Five checks. Each maps to a success criterion and each is mechanical — none
of them is satisfied by reading the diff, which is the point: this feature
exists because rules asserted from inspection drifted.

Stop the AppHost before any build. A running stack holds the service binaries
and the resulting `MSB3027` looks like a broken build rather than a locked file.

---

## 1. The ban fires — and fires *first* (SC-001, FR-008)

Story 1's red. Run this **before** converting the two production sites, so the
failure is observed rather than assumed, and quote the output in the PR.

```powershell
dotnet build SmartSentinelEye.slnx -c Release -v:q --nologo
```

Expect `error RS0030` at
`src/LayoutComposition/Infrastructure/Cameras/CameraCatalogFabGuard.cs:41`
and `:42`, plus the test-fake sites, each naming `Ensure.That` and ADR-0139.

**A green build here means the ban is not wired up.** That is the silent
failure mode research R1 was about: a ban list in the wrong place produces no
error and looks exactly like compliance. Confirm red before proceeding.

Then convert, rebuild, and expect zero `RS0030`.

## 2. Nothing banned survives, and every survivor is an exemption (SC-002)

```powershell
# Expect: only src/AppHost, **/Migrations/*.cs, and Ensure.cs's doc comments.
Select-String -Path (Get-ChildItem -Recurse -Filter *.cs -Path src,tests).FullName `
  -Pattern 'ArgumentNullException\.ThrowIfNull|ArgumentException\.ThrowIfNullOr|ArgumentOutOfRangeException\.ThrowIf' |
  Select-Object -ExpandProperty Path | Sort-Object -Unique
```

`Ensure.cs` appears because it *names* the banned idioms in its XML doc
comments. Prose is not a call site — the analyzer correctly ignores it, and so
should the reviewer.

## 3. The schema did not move (SC-004)

The check that catches an accidental behaviour change most cheaply. Run per
converted context; the diff must be **empty**.

```powershell
dotnet ef migrations add Probe057 `
  --project src/<Context>/Infrastructure `
  --startup-project src/MigrationRunner `
  --output-dir Persistence/Migrations

git status --porcelain    # inspect the generated Up()/Down()
dotnet ef migrations remove --project src/<Context>/Infrastructure --startup-project src/MigrationRunner
```

An empty `Up()` and `Down()` is the pass. **Anything else is a defect, not a
migration to commit** — a value converter that changes the column type means
the retyping was not behaviour-preserving.

Contexts to probe: AuditObservability, Automation, CameraCatalog,
EventIngestion, Identity, LayoutComposition, OverlayDesigner,
StreamDistribution, SystemVariables.

## 4. A stale write is still refused (SC-006)

Story 5's characterization check. Run it **before** retyping the aggregate
(green), then again after (still green). Per aggregate, not per branch.

```powershell
dotnet test tests/<Context>.Application.Tests --filter "FullyQualifiedName~Concurrency"
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~Concurrency"
```

Where no such test exists on a path being retyped, **write it first** (FR-024).
"Green throughout" guaranteed by absent coverage is not a guarantee.

The integration run needs Docker and boots the real Aspire fixture. Mint tokens
from Aspire's proxied endpoint, not the container's mapped port, or everything
401s.

## 5. Every text type refuses `""` (SC-007)

Story 2's red, one per type. Written before the type exists — so it fails to
*compile* first, which is the strongest available red.

```powershell
dotnet test tests/<Context>.Domain.Tests --filter "FullyQualifiedName~ValueObject"
```

Each of the nine types asserts: `""` refused, whitespace refused, over-length
refused where a bound exists, and a valid value round-trips.

---

## Full regression

```powershell
dotnet build SmartSentinelEye.slnx -c Release -v:q --nologo   # analyzers clean, warnings-as-errors
dotnet test SmartSentinelEye.slnx -c Release
./scripts/coverage-check.ps1                                  # Domain ≥ 90, Application ≥ 80, Shared ≥ 90
```

**`-c Release` is not optional, and this was learned the hard way.** Phase 4
was verified against a green Debug build and failed CI on six `CS8602` /
`CS8625` nullable-reference errors that Debug does not raise. Debug is not a
weaker version of the gate — it is a different one, and passing it says
nothing about Release. Nullable value objects are exactly where the two
diverge: an EF converter over a nullable property needs `x => x!.Value`, as
`DeadLetterConfiguration` already did for `fab`.

Coverage matters more than usual here: Story 2 adds nine domain types with
branching factories, all of which land in the ≥ 90% bucket.

---

## What "done" looks like per story

| Story | Done when |
|---|---|
| 1 + 6 | Check 1 red then green; ADR-0139 merged; constitution §II and §Testing amended; CLAUDE.md Phase 4 gate updated |
| 2 | Check 5 green for all nine types; check 3 empty |
| 3 | Check 3 empty; ordering and range queries return identical rows |
| 4 | Malformed input returns 400 (not 500); API tests pass **unmodified** |
| 5 | Check 4 green before and after, for all ten aggregates |

A test that had to be *edited* to pass is a finding. Record it rather than
absorbing it — under the amended rule (Story 6) a red test during a
behaviour-preserving change is a regression, not a step.
