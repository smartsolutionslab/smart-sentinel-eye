# Plan 406: `RtspPath` refuses an authority-less URL

Spec: [spec.md](./spec.md). Issue #3025 item 1.

## 1. Context and layers

- **Bounded context**: none of the nine. `src/ScenarioSimulator` is dev tooling (ADR-0111), a single
  project with no Domain/Application split; constitution §II's primitive rule does not bind it (no domain
  model). No cross-context reference is added; `CameraRegisteredV1` is consumed from `Shared.Contracts`
  as today.
- **Entities / value objects**: none introduced. The input domain of one static parser narrows.
- **Messaging**: unchanged. `CameraRegisteredV1` (integration event) -> `CameraRegisteredSimHandler`. A
  refused URL takes the existing log-and-drop branch: no throw, so no Wolverine retry and no dead letter.
- **Latency**: N/A (spec header).

## 2. The change (one file, one condition)

`src/ScenarioSimulator/CameraSim/RtspPath.cs`, inside `TryExtract`, after `Uri.TryCreate` succeeds and
before the path is read:

```csharp
if (uri.Host.Length == 0)
{
    return false;
}
```

(folded into the existing `if` is equally acceptable; `string.IsNullOrEmpty(uri.Host)` mirrors
`UrlRedaction.ToOrigin` and is also acceptable.) The XML summary gains one sentence saying *why*: an
authority-less URI keeps its dot segments, and the path is interpolated into the camera-sim config API
route. No other production file changes.

**Amendment (security review):** the empty-host check alone is not sufficient (an IPv6 host followed by text,
and non-rtsp schemes with a host, keep their dot segments). After extracting `candidate`, `TryExtract` also
resolves the string the provisioner sends with `Uri.TryCreate(ConfigBase, ConfigPrefix + candidate, out var resolved)` (never the bare candidate, whose first segment can read as a scheme, e.g. `a:b/c`) where `ConfigBase` is
`new Uri("https://camera-sim.invalid" + "/v3/config/paths/add/")`, and returns false unless
`resolved.AbsolutePath` starts with the prefix (ordinal) and is longer than it. The host of the base is
irrelevant to the check; `https` satisfies S5332 without a suppression. The US2 rows pin that every
`rtsp://host/...` path still resolves under the prefix.

**Amendment 2 (security re-run):** the postcondition above proved only the add request, but the provisioner
also sends `/v3/config/paths/replace/{path}` when camera-sim answers the add with "path already exists".
`../add/x` stays under `add/` yet its replace request resolves to `/v3/config/paths/add/x`. `TryExtract` now
requires the check to hold for both verbs: `ConfigRoot = "/v3/config/paths/"` composed with `add/` and
`replace/` constants gives `AddPrefix`/`ReplacePrefix` and one base `Uri` per prefix, and a private
`StaysUnder(base, prefix, candidate)` (`Uri.TryCreate`, ordinal `StartsWith`, non-empty tail) must be true for
both. No suppression, no new throw.

Residual (accepted): a bare dot segment that stays under both prefixes (`rtsp://[::1]./x`) is still
accepted; no dot-segment rule is added, since it cannot leave either prefix.

Why not the provisioner (spec §4): escaping `..` is a no-op, and escaping an already-escaped
`AbsolutePath` double-encodes (`%20` -> `%2520`), splitting camera-sim's path name from the one the main
MediaMTX pulls.

## 3. Boundary and existing-test check

- `RtspPath.TryExtract` has exactly one production caller: `CameraRegisteredSimHandler.cs:38`. No test
  references `RtspPath` directly today (`grep -rn RtspPath tests` finds only StreamDistribution's unrelated
  `RtspPathHealth`).
- `CameraSimReconciler` calls `ProvisionLoopPathAsync(sim.Path, ...)` with scenario config, never via
  `RtspPath`; all twelve asset paths (`Scenarios/*.json`) are `[a-z0-9-]`.
- Existing assertions in conflict: **none found.** The only authority-less URL in the test project is
  `@"rtsp:\\USERMARK:PWMARK#@cam.example/x"` (`CameraRegisteredSimHandlerLogRedactionAdversarialTests.cs:17`,
  `Host` empty, so newly refused). Both theories using it assert only that the camera identifier appears
  in the log and that the markers do not; the skip line carries the identifier and `<unparseable url>`,
  so both stay green. Neither asserts a camera-sim request. The test-writer confirms this by running the
  file on the green commit, unmodified.
- `CameraSimSpanRedactionTests`, `CameraSimProvisionerSpanTests`, `CameraSimProvisionerTests`,
  `CameraRegisteredSimHandlerCharacterisationTests`, `CameraRegisteredSimHandlerLogRedactionTests`: use
  `rtsp://` URLs with hosts or call the provisioner directly; unaffected.

## 4. Tests

New files only (no existing test edited):

1. `tests/ScenarioSimulator.Tests/RtspPathTests.cs` - pure unit tests.
   - `An_authority_less_url_is_not_a_simulated_camera_path` (Theory, spec US1 outline rows) - **RED** today
     (`TryExtract` returns true with the dot-segment path); assert `false` and `path == string.Empty`.
   - `A_url_with_a_host_extracts_the_same_path_as_before` (Theory, spec US2 rows) - **characterisation,
     GREEN** before and after.
2. `tests/ScenarioSimulator.Tests/CameraRegisteredSimHandlerAuthorityLessUrlTests.cs` - through the real
   handler and provisioner via the existing `Fakes/SimHandlerRig` (no new fake).
   - Theory over `rtsp:../../../../SECRET-token` and `rtsp:../SECRET/x`, both rig modes
     (`cameraSimAlreadyHasThePath: false` and `true`): `rig.CameraSim.Requests.ShouldBeEmpty()`; the single
     log entry's message equals the skip template with `UrlRedaction.Unparseable`; `rig.Log.Dump` does not
     contain `SECRET`. **RED** today: the rig records `POST /SECRET-token` (shape 1) and
     `POST /v3/config/paths/SECRET/x` (shape 2), proving the escape through the real `HttpClient`
     resolution. The red output must quote the recorded request path - that is the issue's evidence.
   - `A_scenario_asset_registered_with_a_host_is_provisioned_as_today` (characterisation, GREEN): asset
     `station-4-roughing`, URL `rtsp://camera-sim:8554/station-4-roughing`, request path
     `/v3/config/paths/add/station-4-roughing`. (Overlaps existing coverage on purpose: it sits next to the
     red rows so the reviewer sees both halves in one file.)

Span-level evidence: not a separate test. Once no request is made, there is no span; spec 393's span tests
continue to pin redaction for every request that is made. Adding a ServiceDefaults host to the handler rig
would be new test infrastructure for an assertion the request recorder already makes.

`ScenarioSimulator.Tests` is a unit project with no CI shard filter (shards exist only under
`tests/Integration.Tests/ci-shards/`), so new classes need no filter entry.

## 5. Constitution / ADR alignment

- §Testing: behaviour-changing rows red first; behaviour-preserving rows characterised green, unmodified.
- ADR-0105: no new argument guard (a parse refusal is not a precondition).
- ADR-0050: no new log message; the existing `[LoggerMessage]` skip line is reused.
- ADR-0144: the lane implements, it does not decide: no ADR, no constitution change. The cure choice is a
  `[DECISION]` in spec §4 for the reviewer.
