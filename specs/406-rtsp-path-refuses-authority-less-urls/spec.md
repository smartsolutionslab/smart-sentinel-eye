# Spec 406: The camera-sim provisioning request cannot leave `/v3/config/paths/<verb>/`; `RtspPath` refuses an authority-less URL

**Feature Branch**: `fix/3025-camera-sim-segment-escape` (cut from `origin/develop` at `c8c3773c`)

**Created**: 2026-10-18

**Status**: Phases 1-3 written. No `[NEEDS CLARIFICATION]` remains. One `[DECISION]` (§4, the cure)
departs from the lane-scope comment's named cure, with evidence; it is the reviewer's to strike.

**Input**: Issue [#3025](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3025) item 1 only,
found by the security review of #3019 (spec 393). Item 2 (`CameraSimPathClient`'s `GET /v3/paths/get/{path}`
span and its `CameraSimPathUnreadable` log) is **out**: its only caller passes scenario-asset paths.

**ADRs.** 0111 (Scenario Simulator, dev-only, camera-sim), 0050 (logging), 0118 (one telemetry sink: the
Aspire dashboard is where the span is read), 0139/0144 (red first, autonomous lane). **No new ADR**: this
closes a hole in spec 393's already-taken decision (the camera-sim client span exports the verb, never the
operator-typed path) by tightening a parser's input domain. Nothing in `ServiceDefaults`, `Shared.Kernel`
or `Shared.Contracts` changes.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (phase 4a RED) for authority-less URLs.
**Behaviour-preserving** (characterisation, GREEN before and after) for every `rtsp://host/...` URL and every
scenario-asset path.

**Latency budget (constitution §IV): N/A.** The Scenario Simulator is dev tooling, off outside run mode and
off in the E2E lane; no leg of the event-to-overlay path runs through it.

## 1. Premise, verified on `c8c3773c`

`CameraRegisteredSimHandler.Handle` -> `RtspPath.TryExtract(url)` (`Uri.TryCreate(..., Absolute)`, then
`AbsolutePath.Trim('/')`) -> `CameraSimProvisioner.ProvisionAsync` interpolates it unescaped into
`$"/v3/config/paths/add/{path}"` (and `replace/`). `HttpClient` resolves that relative URI against
`BaseAddress`, collapsing dot segments. Probed with a net10.0 console on this machine (same `Uri` +
`HttpClient` resolution the provisioner uses):

| Input URL | `uri.Host` | Extracted path | Request URL the span exports | Spec 393 `Redact` |
|---|---|---|---|---|
| `rtsp:../../../../SECRET-token` | empty | `../../../../SECRET-token` | `http://camera-sim:9997/SECRET-token` | unchanged: **leaks** |
| `rtsp:../SECRET/x` | empty | `../SECRET/x` | `http://camera-sim:9997/v3/config/paths/SECRET/x` | `SECRET` kept as the "verb": **leaks** |
| `rtsp://cam/../../../../SECRET` | `cam` | `SECRET` | `.../v3/config/paths/add/SECRET` | redacted |
| `rtsp://cam/a/%2E%2E/%2E%2E/%2E%2E/%2E%2E/SECRET` | `cam` | `SECRET` | `.../v3/config/paths/add/SECRET` | redacted |
| `rtsp://cam/.%2E/.%2E/.%2E/.%2E/SECRET` | `cam` | `SECRET` | `.../v3/config/paths/add/SECRET` | redacted |
| `rtsp://cam/a%2F..%2F..%2F..%2F..%2FSECRET` | `cam` | `a%2F..%2F..%2F..%2F..%2FSECRET` | `.../v3/config/paths/add/a%2F..%2F...` | redacted |

So the premise holds exactly as filed: **only authority-less URIs keep their dot segments**; with an
authority `Uri` removes them (literal or `%2E`-encoded) before `AbsolutePath` is read, and an encoded `/`
stays encoded, so the request stays under the prefix.

**Not reachable today**: `RtspUrl.From` (`src/CameraCatalog/Domain/Camera/RtspUrl.cs:46`) requires `rtsp://`
and a non-blank host, and the only producer of `CameraRegisteredV1`
(`CameraRegisteredDomainEventHandler.cs:20`) goes through it. A forged broker message is the only route.

## 2. User stories and acceptance scenarios

### US1 (P1) - An authority-less camera URL never becomes a camera-sim request

As a developer reading the Aspire dashboard, I can rely on spec 393's guarantee - the camera-sim provisioning
span exports `/v3/config/paths/<verb>/Redacted` and nothing operator-typed - for every `CameraRegisteredV1`
the simulator accepts, not only for the URL shapes the catalog happens to produce today.

```gherkin
Scenario: Dot segments in an authority-less URL cannot climb out of the config API (the issue's shape)
  Given the real CameraRegisteredSimHandler over a recording camera-sim
  When a camera C is registered at "rtsp:../../../../SECRET-token"
  Then camera-sim receives no request
  And the single log line is "Camera C at <unparseable url> has no path component; not a simulated camera, skipping."
  And no log record contains "SECRET-token"

Scenario: A dot segment cannot promote the path to the verb position (the issue's second shape)
  When a camera C is registered at "rtsp:../SECRET/x"
  Then camera-sim receives no request
  And no log record contains "SECRET"

Scenario Outline: Every authority-less shape is refused (bad request)
  When RtspPath.TryExtract is given "<url>"
  Then it returns false and the path is empty
  Examples:
    | url                           |
    | rtsp:../../../../SECRET-token |
    | rtsp:../SECRET/x              |
    | rtsp:SECRET/x                 |
    | rtsp:/SECRET/x                |
    | rtsp:///SECRET/x              |
    | rtsp:..                       |
    | file:///../../SECRET          |
    | urn:x:SECRET                  |
```

Conflict (replace branch): with camera-sim answering `400 "path already exists"` to every add, the two
issue shapes still produce **no** request (neither add nor replace).

Auth: N/A - the camera-sim control plane is unauthenticated on the docker network; no token or scope is
touched. The trust boundary is the broker message, which this tightens.

### US2 (P1) - Every URL the catalog can produce, and every scenario asset, is byte-identical (characterisation)

```gherkin
Scenario Outline: A hierarchical rtsp:// URL extracts exactly what it does today
  When RtspPath.TryExtract is given "<url>"
  Then it returns true with path "<path>"
  Examples:
    | url                                               | path                            |
    | rtsp://camera-sim:8554/station-4-roughing         | station-4-roughing              |
    | rtsp://camera-sim:8554/electronics-moulding       | electronics-moulding            |
    | rtsp://camera-sim:8554/paper-press-group          | paper-press-group               |
    | rtsp://cam/SECRET-token/stream                    | SECRET-token/stream             |
    | rtsp://cam/a b/c                                  | a%20b/c                         |
    | rtsp://cam/caf%C3%A9                              | caf%C3%A9                       |
    | rtsp://cam/../../../../SECRET                     | SECRET                          |
    | rtsp://cam/a/%2E%2E/%2E%2E/%2E%2E/%2E%2E/SECRET   | SECRET                          |
    | rtsp://cam/a%2F..%2F..%2F..%2F..%2FSECRET         | a%2F..%2F..%2F..%2F..%2FSECRET  |
    | rtsp://[fe80::1%25eth0]:8554/P                    | P                               |

Scenario: A scenario asset camera's request and log lines are byte-identical to today
  Given asset "station-4-roughing" playing "roughing.mp4"
  When its camera is registered at "rtsp://camera-sim:8554/station-4-roughing"
  Then camera-sim receives "POST /v3/config/paths/add/station-4-roughing"
  And the existing asset log lines are written unchanged
```

The second scenario is already pinned by existing tests (`A_scenario_asset_camera_logs_its_asset_path_as_today`,
`CameraSimProvisionerTests`); they must pass **unmodified**. `CameraSimReconciler` passes asset paths
straight to `ProvisionLoopPathAsync` without `RtspPath`, so it is untouched by construction.

## 3. Functional requirements

- **FR-001** `RtspPath.TryExtract` returns `false` (path empty) for any absolute URI whose `Host` is empty.
- **FR-002** For any URI with a non-empty host, `TryExtract`'s result is unchanged (same boolean, same path,
  byte for byte).
- **FR-003** `CameraSimProvisioner` is **not changed**: the request path it builds for any path it is given
  today is byte-identical (asset paths, multi-segment operator paths, percent-encoded paths).
- **FR-004** The handler's refusal goes through its existing log-and-drop branch
  (`SkippedNonSimulatedCamera`, origin via `UrlRedaction.ToOrigin`, which already answers
  `<unparseable url>` for an empty host). No new log message, no new event, no retry.
- **FR-005** Spec 393's tests (`CameraSimSpanRedactionTests`, `CameraSimProvisionerSpanTests`) and every
  existing `ScenarioSimulator.Tests` assertion pass unmodified. `CameraSimSpanRedaction` is not changed.

## 4. The cure `[DECISION]`

The issue names two cures; the lane-scope comment picked the first. **This spec takes the second**, because
the probe in §1 shows the first does not close the hole and changes behaviour that must not change:

1. **`Uri.EscapeDataString` per segment in `ProvisionAsync` - rejected.**
   - It does not cure the defect: `.` is an unreserved character, so `Uri.EscapeDataString("..")` returns
     `..` and the request still resolves to `http://camera-sim:9997/SECRET-token` (probed: rows "perSeg"
     for both issue shapes are identical to "raw").
   - It double-escapes paths that are already escaped: `uri.AbsolutePath` is the *escaped* form, so
     `rtsp://cam/a b/c` -> `a%20b/c` -> request `add/a%2520b/c`. camera-sim would then create the path
     `a%20b/c` while the main MediaMTX pulls `a b/c`: a camera with no video. Same for `%C3%A9` and any
     `%XX`. That is a behaviour change on hierarchical URLs the catalog accepts today.
   - Escaping the *whole* path as `CameraSimPathClient` does (`Uri.EscapeDataString(path)`) is worse still:
     it turns `/` into `%2F`, so the multi-segment path spec 393's span tests pin
     (`POST /v3/config/paths/add/SECRET-token/stream`) would change, breaking FR-005.
   - Making it work would need unescape-then-escape per segment **plus** a dot-segment refusal - more code
     than option 2 and still a change at the provisioner every asset passes through.
2. **`RtspPath.TryExtract` refuses a URI with an empty host - chosen.** One guard at the one place untrusted
   text enters (the broker message), one caller (`CameraRegisteredSimHandler`). Every input the catalog can
   produce has a host, so for them it is a no-op; with a host, `Uri` has already removed every dot segment
   (§1), so the path cannot climb. Asset paths never pass through `RtspPath`. Smaller and safer.

"Authority-less" is tested as `uri.Host` empty rather than `uri.Authority` empty: they coincide for every
shape probed, `Host` is the property `UrlRedaction.ToOrigin` already keys its `<unparseable url>` on, and a
URI with an authority but no host is not a camera. `mailto:a@b` has a host (`b`) and an empty path, so it is
already refused by the existing empty-path rule.

## 5. Out of scope (the lane must not take)

- Issue item 2: `CameraSimPathClient` span and `CameraSimPathUnreadable` log.
- Any change to `CameraSimProvisioner`, `CameraSimSpanRedaction`, `CameraSimPathClient`, `UrlRedaction`,
  `RtspUrl` or the `SkippedNonSimulatedCamera` wording ("has no path component" is slightly loose for an
  authority-less URL; rewording is a log-template change nobody asked for).
- Server-side handling inside camera-sim of an encoded `%2F..` path that stays under the prefix (row 6 of
  §1): the span stays redacted, which is this spec's concern; what MediaMTX does with such a name is not.
- Escaping in the provisioner as defence in depth: §4 shows it changes asset-adjacent behaviour.

## 6. Independent end-to-end test procedure

Unit-level is end-to-end here: the defect is a pure function of `Uri` resolution inside one process, and the
real handler, real provisioner and real `HttpClient` resolution run in `SimHandlerRig` with only the socket
replaced. Steps:

1. `dotnet test tests/ScenarioSimulator.Tests/SmartSentinelEye.ScenarioSimulator.Tests.csproj --filter
   "FullyQualifiedName~RtspPathTests|FullyQualifiedName~CameraRegisteredSimHandlerAuthorityLessUrlTests"`
   on the red commit: the US1 rows fail (request recorded at `/SECRET-token`; `TryExtract` returns true),
   US2 rows pass.
2. Same filter after the fix: all pass.
3. Whole `ScenarioSimulator.Tests` project: green, no test file other than the two new ones in the diff.

No Aspire boot is required (dev-only tooling, no stack behaviour changes for any producible URL).

## 7. Assumptions

- A1: `.NET 10`'s `Uri` keeps dot segments for authority-less absolute URIs and removes them (literal and
  `%2E`) when a host is present - observed on this machine, pinned by the US2 characterisation rows so a
  runtime change would show as a red test rather than a silent regression.
- A2: Nothing legitimate registers a simulated camera with an authority-less URL (the only producer is
  `RtspUrl.From`-validated; the simulator's own seeding builds `rtsp://{RtspHost}/{cameraPath}` in
  `CameraCatalogClient.cs:42` and registers it through the catalog, so an empty `RtspHost` is already
  refused by `RtspUrl.From` before any event exists).
