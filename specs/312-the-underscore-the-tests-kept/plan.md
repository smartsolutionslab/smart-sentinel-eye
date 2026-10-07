# Plan 312 — The underscore the tests kept

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2556

## 1. Shape

No bounded context, no domain model, no messaging, no contract. Test-support code in 18 test
projects is renamed; no production assembly changes. NetArchTest boundaries are unaffected (no
project reference moves).

Change class: **behaviour-preserving** (constitution §Testing, ADR-0139). Phase 4a is
**characterisation, observed green** — the existing suites are the covering tests; they are
captured green on develop and must pass **unmodified** afterwards. No new test is written, because
there is no new behaviour to pin; a test asserting "this field has no underscore" would assert the
diff, not the system.

## 2. Granularity — one issue, one PR, three code commits

Not split into four issues/PRs. Reasons:

- **Each rename is file-local.** Private fields are invisible outside their type, so the only
  cross-file coupling is the `AspireFixture` partial (`.cs`, `.Auth.cs`, `.LogCapture.cs`). Review
  cost is per hazard (spec §2), not per file, and the hazard list is complete and short.
- **Four PRs buy nothing and cost a lot.** Each would run the 20-minute-plus integration + e2e CI,
  and occupy one of the lane's three parked-PR slots (ADR-0144), for diffs a reviewer reads the
  same way.
- **Bisectability is kept by commits instead** (ADR-0087): one commit per group, each of which
  builds and passes on its own, because no group references another group's fields.

| Commit | Files | Fields | Hazards |
|---|---|---|---|
| `refactor(2556): drop leading underscore from test builder fields` | 11 builders | 70 | H1 ×21, H2 ×18, H3 ×1 |
| `refactor(2556): drop leading underscore from test fakes and repositories` | 26 fakes | 36 | H1 ×2 (`FakeClock`, `FakeCameraFabGuard`) |
| `refactor(2556): drop leading underscore from integration fixture fields` | `AspireFixture.cs`, `.Auth.cs`, `.LogCapture.cs`, `KeycloakAdminTokenProviderTests.cs`, `AspireFixtureReportSelectionTests.cs` (comment) | 8 | H1 ×1 (`fixture`), H4, H5 |

The fixtures commit **must** include `AspireFixture.LogCapture.cs` (two `_logTails` reads, no
declaration) or it does not compile.

## 3. File inventory (fields per file)

**Builders** — `AuditObservability.Application.Tests/TestData/AuditEventBuilder.cs` (11),
`Automation.Domain.Tests/Rule/RuleBuilder.cs` (8), `CameraCatalog.Domain.Tests/Camera/Builders/CameraBuilder.cs` (5),
`EventIngestion.Domain.Tests/Event/EventBuilder.cs` (8), `…/RegisteredEventType/RegisteredEventTypeBuilder.cs` (4),
`…/SourceMode/SourceModeBuilder.cs` (5), `Identity.Domain.Tests/RegisteredClient/RegisteredClientBuilder.cs` (5),
`LayoutComposition.Domain.Tests/Layout/Builders/LayoutBuilder.cs` (8),
`OverlayDesigner.Domain.Tests/Overlay/Builders/OverlayBuilder.cs` (4),
`StreamDistribution.Domain.Tests/Stream/Builders/StreamBuilder.cs` (5),
`SystemVariables.Domain.Tests/Variable/Builders/VariableBuilder.cs` (7).

**Fakes** — AuditObservability.Application.Tests/Fakes: `FakeAuditChunkArchiver.cs` (2: `_store`,
and `_chunks` in the second type `FakeAuditChunkInventory`), `FakeBus.cs`, `FakeClock.cs`,
`InMemoryAuditEventRepository.cs` (2). Automation.Application.Tests/Fakes: `CapturingLogger.cs`,
`FakeEventBus.cs`, `InMemoryRuleCache.cs` (2: `_byTrigger`, `_gate`), `InMemoryRuleRepository.cs`.
CameraCatalog.Application.Tests/Fakes: `FakeEventBus.cs`, `InMemoryCameraRepository.cs` (2).
EventIngestion.Application.Tests/Fakes: `FakeEventBus.cs`, `InMemoryEventRepository.cs`,
`InMemoryRegisteredEventTypeRepository.cs` (2), `InMemoryWebhookIntegrationRepository.cs` (2).
Identity.Application.Tests/Fakes: `FakeEventBus.cs`, `FakeKeycloakAdminClient.cs`,
`InMemoryRegisteredClientRepository.cs` (2). LayoutComposition.Application.Tests/Fakes:
`FakeCameraFabGuard.cs` (2), `InMemoryLayoutRepository.cs`. OverlayDesigner.Application.Tests/Fakes:
`InMemoryOverlayRepository.cs`. StreamDistribution.Application.Tests/Fakes: `CapturingLogger.cs`,
`FakeRtspGateway.cs`, `InMemoryStreamRepository.cs` (2). SystemVariables.Application.Tests/Fakes:
`CapturingLogger.cs`, `InMemoryReverseIndex.cs` (2), `InMemoryVariableRepository.cs`.
(Unmarked = 1 field.)

**Fixtures** — see the commit table.

**Affected test projects (18)** — the Domain.Tests and Application.Tests of AuditObservability
(Application only), Automation, CameraCatalog, EventIngestion, Identity, LayoutComposition,
OverlayDesigner, StreamDistribution, SystemVariables; plus `Integration.Tests`.

## 4. Mechanical checks (phase 5 evidence)

Both must print nothing after the sweep:

```sh
# 1. any leading-underscore field left (tuple types included — the issue's pattern missed them)
grep -rnE --include=*.cs '^\s*(\[[^]]*\]\s*)?((private|protected|internal|public|static|readonly|volatile)\s+)+.*\s_[a-zA-Z]\w*\s*(=|;)' tests | grep -v '/obj/'

# 2. any parameter assigned to itself (H1 escaping review)
grep -rnE --include=*.cs '(^|[^.[:alnum:]_])([a-z][[:alnum:]]*) = \2;' tests | grep -v '/obj/'
```

**Prove check 2 by counterfactual** before trusting it: temporarily write `fab = fab;` into one
builder, see it match, revert (memory: *prove a guard by counterfactual*). The Release build is
the second, independent mechanism (CS1717 is an error there).

## 5. Risks

- **Debug build hides H1.** Every build and test run in phase 4b/5 is `-c Release`.
- **Stale MSBuild outputs** — a test that "still passes" against an old binary. Release from a
  clean `bin/obj` for the 18 projects, or check the build actually recompiled them.
- **`src/…/InMemoryRuleCache.cs:_byTrigger`** is the same violation in production code and is
  **out of scope** (spec §1.3). Recorded on #2764, not fixed here.
