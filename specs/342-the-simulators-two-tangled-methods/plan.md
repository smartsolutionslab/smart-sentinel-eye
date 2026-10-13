# Plan 342: The simulator's two tangled methods

**Spec**: [spec.md](./spec.md). **Issue**: #2897. **ADRs**: 0084, 0144, 0111, 0036.

## 1. Constitution and ADR alignment

- **ADR-0084.** The metrics stay advisory warnings; this spec lowers two methods under the limits
  and touches no threshold, suppression, `.editorconfig` or `SonarLint.xml`.
- **ADR-0144 / §Testing.** Behaviour-preserving, so phase 4a is characterisation observed green;
  tests land in their own commits, before the production commits, and are not edited afterwards.
- **ADR-0111.** The simulator is dev-only. `AppHost.cs` is not touched, so where the simulator
  starts and the `ScenarioSimulator=false` switch are unchanged by construction;
  `AppHostE2ESwitchTests` is run unmodified as the issue requires.
- **No new ADR.** Nothing is decided here; two private methods change shape.
- **Bounded contexts / Shared.Contracts.** Not involved. `ScenarioSimulator` is a worker host
  outside the nine contexts; no project reference changes.

## 2. Shape

| File | Change |
|---|---|
| `tests/ScenarioSimulator.Tests/ClipManifestValidationOrderingTests.cs` | **New.** Exact-list characterisation of `Validate` (spec §2 scenarios 1-4). |
| `tests/ScenarioSimulator.Tests/SimulatorRuntimeBindingTests.cs` | **New.** Characterisation of `BindRuntime` via reflection (spec §6, option A). |
| `src/ScenarioSimulator/Cues/ClipManifestValidation.cs` | Extract named private static methods from `ValidateOneCue`. |
| `src/ScenarioSimulator/Program.cs` | Extract static local functions from `BindRuntime`. |

The existing `ClipManifestValidationTests.cs` is **not** edited (FR-007). No new test class needs a
CI shard entry: `ci-shards/*.filter` partitions `Integration.Tests` only.

## 3. Production shape

### 3.1 `ClipManifestValidation`

Constraints, binding:

- The order of checks per cue is fixed: range, ascending, class, confidence, box, source, kind.
  The loop order over cues is fixed. A null entry still adds `"Cue must not be null."` and still
  does **not** update `previousAtMs`.
- Every message string and every interpolation stays byte-identical (including the culture-default
  `{cue.Confidence}` interpolation).
- Each predicate keeps its exact boolean logic, including short-circuit order (for example
  `durationMs > 0 && cue.AtMs >= durationMs`, and `IsNullOrWhiteSpace(Kind) || Kind.Length > 128`,
  whose order is what keeps a null `Kind` from throwing).
- `Validate` and `ValidateCues` keep their signatures; `CueViolation` and the constants are untouched.

Recommended shape (the engineer may deviate within the constraints; the characterisation decides):

- Predicates as named private static methods returning `bool`, for example `IsOutOfRange(int atMs,
  int durationMs)`, `IsNotAscending(int atMs, int? previousAtMs)`, `IsOutsideUnitInterval(double)`,
  `ExtendsBeyondFrame(CueBox? box)`, `IsKnownSource(string? source)`, `IsValidKind(string? kind)`.
- Split the seven checks into a timing group (range, ascending) and a content group (class,
  confidence, box, source, kind), called in that order. If the loop in `ValidateCues` calls
  `AddRangeViolation(cue, index, durationMs, violations)`, `AddOrderViolation(cue, index,
  previousAtMs, violations)` and `ValidateCueContent(cue, index, violations)` in that order, every
  method has <= 4 parameters and the S107 hit clears too, with no new type. If the engineer keeps a
  five-parameter `ValidateOneCue`, S107 stays and the PR says so; it is not a done criterion.
- Expected: `ValidateOneCue` (or its replacement) <= 10 complexity and <= 30 lines; every new method
  well under both.

### 3.2 `BindRuntime` in `Program.cs`

Constraints, binding:

- `BindRuntime` keeps its name and its `(IConfiguration config, SimulatorOptions options)` signature
  and stays a static local function (the characterisation finds it by name, spec §6).
- Assignments happen in today's order: CameraCatalogUrl, CameraSimApiUrl, OverlayDesignerUrl,
  AutomationUrl, LayoutCompositionUrl, SystemVariablesUrl, MqttHost, KeycloakUrl, Realm, ClientId,
  ClientSecret, RtspHost. Today's order is what decides which missing setting is reported first.
- `??` semantics stay: an empty string is a value, not an absence (no `IsNullOrEmpty`).
- Each missing-setting message is passed **verbatim**; do not derive it from the key (the wording is
  irregular: the mosquitto one says "MQTT host", the Keycloak and secret ones carry no key).
- Helpers are `static` local functions in `Program.cs`, no underscore, one parameter per line when
  wrapping. No new class.

Recommended shape:

- `static string RequiredSetting(IConfiguration config, string aspireKey, string runtimeKey, string missingMessage)`
  returning `config[aspireKey] ?? config[runtimeKey] ?? throw new InvalidOperationException(missingMessage)`
  (four parameters: at the S107 limit, not over it). Used for the seven endpoints.
- `static string ResolveKeycloakUrl(IConfiguration config)` for the three-way Keycloak chain.
- Realm, ClientId, RtspHost, ClientSecret stay inline (complexity 4).
- Expected: `BindRuntime` about 5 complexity and about 25 lines; helpers 3 and 4.

The `InvalidOperationException` throws already exist; moving one into a helper does not touch the
ADR-0105 guard rule (these are configuration failures, not argument preconditions).

## 4. Characterisation tests

### 4.1 `ClipManifestValidationOrderingTests`

Assert with Shouldly on the whole list (`violations.ShouldBe([...])`: `CueViolation` is a record,
so equality is by value and order). Cases, all with integer-valued `Confidence` so the expected
text is culture-independent:

1. Manifest-level trio: clip mismatch, `DurationMs` 0, `Cues` null: exactly three, in that order.
2. Clip mismatch plus empty `Cues`: mismatch, then `"Cues must declare at least one cue."`.
3. Density: 11 cues in a 1000 ms clip: exactly `"11 cue(s) exceed the density limit of 10 for a 1000ms clip."`.
4. One cue (index 1, after a valid index 0 at `AtMs` 0) breaking all seven rules: `AtMs` -5,
   `Class` blank, `Confidence` 2, `Box` (0.9, 0.1, 0.5, 0.1), `Source` "camera", `Kind` "": exactly
   seven violations in order range, ascending, class, confidence, box, source, kind.
5. Cross-cue order and the null entry: `[AtMs 5000, null, AtMs 1000]` gives index 1 null, then
   index 2 `"AtMs 1000 is not strictly ascending after 5000."`.
6. Boundaries, each as an exact list (empty where accepted): `AtMs == DurationMs` refused;
   `AtMs` 0 and `DurationMs - 1` accepted; equal consecutive `AtMs` refused; `Confidence` 0 and 1
   accepted; `Box` (0.5, 0.5, 0.5, 0.5) accepted; null `Box` accepted; negative `Box.Height`
   refused; `Source` "plc" accepted; null `Source` gives `"Source '' must be 'plc' or 'inference'."`;
   128-character `Kind` accepted; whitespace and null `Kind` refused; `DurationMs` 0 with a cue at
   50 000 gives only the `DurationMs` violation (range and density both skipped).

### 4.2 `SimulatorRuntimeBindingTests`

Helper: `typeof(SimulatorOptions).Assembly.EntryPoint!.DeclaringType!`, then the single
`NonPublic | Static` method whose name contains `"g__BindRuntime|"` (ordinal); invoke with
`BindingFlags.DoNotWrapExceptions` so the test sees the real `InvalidOperationException`.
Configuration from `new ConfigurationBuilder().AddInMemoryCollection(...)` (`ConnectionStrings:keycloak`
for the connection string). Cases:

1. Aspire keys and fallback keys both present for every endpoint: every option takes the
   `services:` value; `Realm`, `ClientId`, `RtspHost` keep their defaults.
2. Fallback keys only (and Keycloak via `services:keycloak:https:0`): every option takes the
   `ScenarioSimulator:Runtime:` value.
3. Keycloak precedence (theory): connection string over http over https.
4. `Realm`, `ClientId`, `RtspHost` read from `ScenarioSimulator:Runtime:*` when set.
5. An empty-string `services:camera-catalog:http:0` beside a non-empty fallback yields `""`.
6. Each required setting missing in turn (theory, nine rows): exact message.
7. Empty configuration: the camera-catalog message.

## 5. Measurement commands (before and after, quoted in the PR)

```sh
dotnet build src/ScenarioSimulator -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning S1541' | sort -u
```

Before (2026-10-13, `5ecc3007`): `ClipManifestValidation.cs(97,25)` and `Program.cs(125,13)`.
After: no `ScenarioSimulator` line. The same build also lists `ServiceDefaults` hits (a referenced
project); none is S1541 today, and none is in scope. For FR-002, rerun with `S138|S107` in place of
`S1541` and compare the two files' lines.

```sh
dotnet test tests/ScenarioSimulator.Tests -c Release
dotnet test tests/ScenarioSimulator.Tests -c Release --no-build --filter "FullyQualifiedName~ClipManifestValidation|FullyQualifiedName~SimulatorRuntimeBinding"
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~AppHostE2ESwitchTests"
dotnet test tests/Architecture.Tests -c Release
```

Baseline 2026-10-13: `ScenarioSimulator.Tests` 162 passed; `ClipManifestValidationTests` 21 passed.

## 6. Risks

- **Reflection on a compiler name** (spec §6). Fails loudly if Roslyn's scheme changes; accepted.
- **A predicate inverted during extraction** (for example `IsKnownSource` returning the opposite
  sense of today's `is not (...)`). Exactly what §4.1's accepted-and-refused boundary pairs catch.
- **Short-circuit reorder** turning a null `Kind` or null `Source` into a `NullReferenceException`.
  Pinned by the null cases.
- **Architecture guards** that scan `src/` generically (`ServedHealthHostCompletenessTests` reads
  every `Program.cs` for `MapDefaultEndpoints(`; `ResilienceRegistrationTests` reads
  `RetryEveryMethod()` call sites). Neither text changes; the full `Architecture.Tests` run in
  phase 5 confirms it.
