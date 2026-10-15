# Plan 372: The simulator's `BindRuntime` under thirty lines

**Spec**: [spec.md](./spec.md). **Issue**: #2963. **ADRs**: 0084, 0144, 0111, 0036.

## 1. Context and layers

`src/ScenarioSimulator` is a dev-only worker host (ADR-0111), not a bounded context: no
Domain/Application/Infrastructure split, no `Shared.Contracts` change, no messaging change, no
AppHost change. The only production file touched is `src/ScenarioSimulator/Program.cs`. No
entities, value objects or invariants change; `SimulatorOptions` is untouched. Boundary rules are
unaffected (no project references change). Constitution §II does not reach here (no domain model).

## 2. Current shape (`8102af6f`, `Program.cs:125-177`, S138 = 42)

```
BindRuntime(config, options)
  CameraCatalogUrl     = RequiredSetting(…)   5 lines
  CameraSimApiUrl      = RequiredSetting(…)   5
  OverlayDesignerUrl   = RequiredSetting(…)   5
  AutomationUrl        = RequiredSetting(…)   5
  LayoutCompositionUrl = RequiredSetting(…)   5
  SystemVariablesUrl   = RequiredSetting(…)   5
  MqttHost             = RequiredSetting(…)   5
  KeycloakUrl          = ResolveKeycloakUrl(config)
  Realm / ClientId     = config[…] ?? default
  ClientSecret         = config[…] ?? throw
  RtspHost             = config[…] ?? default
```

## 3. Target shape

Three new `static` local functions next to `BindRuntime`, each `(IConfiguration config,
SimulatorOptions options)` (2 parameters; S107-clean), statements moved **verbatim and in order**:

```
BindRuntime(config, options)                 ~11 lines
  BindCameraEndpoints(config, options)
  BindSeedingEndpoints(config, options)
  options.MqttHost = RequiredSetting(…)       (unchanged, stays here to keep its position)
  BindIdentity(config, options)
  options.RtspHost = config[…] ?? "camera-sim:8554"

BindCameraEndpoints   CameraCatalogUrl, CameraSimApiUrl                       ~11 lines
BindSeedingEndpoints  OverlayDesignerUrl, AutomationUrl,
                      LayoutCompositionUrl, SystemVariablesUrl                ~23 lines
BindIdentity          KeycloakUrl, Realm, ClientId, ClientSecret (?? throw)   ~6 lines
```

Invariants the engineer must hold (each pinned by a test in spec §4):

- **I1 Order.** Resolution order of the nine throwing settings is unchanged (C1 + existing order fact).
- **I2 Keys/defaults/messages.** Every string literal moves character-for-character; no literal is
  re-typed, merged into a table, or interpolated.
- **I3 `??` semantics.** Null-coalescing stays null-coalescing; no `IsNullOrEmpty`/`IsNullOrWhiteSpace`
  (C2 + empty-value fact).
- **I4 Seam.** `BindRuntime` keeps name and signature; no helper name makes its generated name
  contain `g__BindRuntime|` (the names above do not).
- **I5 Visibility.** Nothing is moved out of `Program.cs`, nothing becomes `internal`/`public`.

The header comment above `BindRuntime` (Aspire key shapes) stays where it is; no new comments
unless a *why* is non-obvious (CLAUDE.md "no drive-by comments").

`RtspHost` stays last in `BindRuntime` rather than moving into `BindCameraEndpoints`: it never
throws, so moving it would be unobservable through `IOptions`, but it would change the state the
options object is left in when a later setting throws. Not moving it is the smaller change.

## 4. Alternatives rejected

- **One `BindServiceEndpoints` (all seven)** — 35 lines, still S138 (spec §5).
- **Table-driven loop over `(aspireKey, runtimeKey, message, setter)`** — clears S138, but re-types
  every literal into a new structure and introduces setter delegates; a rewrite, not an extraction.
- **Move `BindRuntime` into a `SimulatorRuntimeBinding` class** — removes the reflection seam but
  forces edits to the existing suite (violates FR-006) and widens visibility.
- **Reorder so mosquitto joins the camera group** — reorders failure reporting (I1).

## 5. Measurement commands

```sh
# S138 (before: 1 hit at 42; after: 0)
dotnet build src/ScenarioSimulator -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 | grep -E "warning S(138|107|1541|134)" | grep "ScenarioSimulator" | sort -u

# Characterisation (before: 20 passed; after C1/C2: 30 passed, before and after refactor)
dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~SimulatorRuntimeBindingTests"

# Whole project (before: 242 passed)
dotnet test tests/ScenarioSimulator.Tests -c Release
```

S107 count must stay 15 and no S1541/S134 hit may appear in `Program.cs`.

## 6. Constitution / ADR check

- §Testing: behaviour-preserving -> characterisation observed green, unmodified after. Satisfied by
  the existing suite + C1/C2 added first.
- ADR-0084: advisory limits; this clears one. No threshold, suppression, or `.editorconfig` change.
- ADR-0036: one file, shape only.
- §IV latency: N/A (dev-only host, start-up path).
- No ADR needed; no constitution amendment.
