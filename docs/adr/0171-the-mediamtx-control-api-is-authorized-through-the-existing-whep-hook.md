# ADR-0171: MediaMTX's control API is authorized through the existing WHEP hook

**Status:** Accepted
**Date:** 2026-10-11
**Supersedes:** —
**Superseded by:** —

## Context

MediaMTX's control API (port 9997) is excluded from `authMethod: http`
entirely (`authHTTPExclude: {action: api}` in `mediamtx.yml`), so any
caller that can reach the port can enumerate every camera's RTSP source
(including embedded credentials), redirect or delete any stream's path,
register a new path with `runOnInit`/`runOnReady`/`runOnDemand` hooks
(shell command execution inside the one container that bridges the
camera OT network and the IT network), and flip `authMethod`/
`authHTTPExclude` itself — undoing ADR-0161's WHEP authorization for
every stream in one call (#2659).

Today only `localhost` can reach it (confirmed against the running
Aspire stack: 9997 is bound `127.0.0.1` only, never passed to
`WithExternalHttpEndpoints`). No production deployment or Helm chart
exists yet for the SFU (ADR-0130, decision row 025), so there is no
network-level isolation built for the eventual k3s topology, and the
SFU's documented dual-NIC role as the OT/IT bridge (decision row 013)
means the control API sits on both networks unless a future deployment
explicitly isolates it.

## Decision

**Route `api` actions through the authorization hook StreamDistribution
already serves for WHEP** (`AuthorizeWhepCommandHandler`), rather than
building a new auth component or changing `authMethod`.

- Remove `action: api` from `authHTTPExclude`. MediaMTX then POSTs every
  control-API call to the existing `/streams/authorize` hook with
  `action: "api"`, alongside whatever credential the caller supplied.
- The hook gains one branch: `api` is authorized only against a shared
  secret (an Aspire `AddParameter(..., secret: true)` injected into
  `stream-distribution`), compared in constant time, checked **before**
  any WHEP-token validation — mirroring the existing refusal shape for
  the `Publish` action.
- The secret is never stored by MediaMTX; it is forwarded per call, so
  it survives StreamDistribution running multiple replicas (a
  per-process secret would not).
- `metrics` (port 9998) stays excluded from auth — it is read-only and
  the information it leaks (path names) is the same class already
  accepted for other read surfaces; isolating it is covered by the
  network requirement below, not this hook.

**A Keycloak-backed alternative (client-credentials token, a new
`sse.sfu.manage` scope) was considered and rejected for now**: better
audit trail and rotation, but a new control-plane dependency on
Keycloak, JWT validation cost on every call (the health sweep alone
calls the API roughly every 2s per stream — about 125 calls/sec at 250
cameras), and 2-3x the implementation cost of the shared-secret
approach for a control surface with few, non-human callers today. Worth
revisiting if per-caller audit becomes a real requirement.

**Network-level isolation of port 9997 is recorded as a deployment
requirement, not built now**: no SFU Helm chart or k3s deployment exists
yet (ADR-0130 row 025) to isolate it in, and the SFU's likely
`hostNetwork` placement on GPU nodes makes CNI-level NetworkPolicy
unreliable as the sole control anyway. The shared-secret hook check is
the actual closed gap; this requirement is defence in depth for when
the deployment exists.

## Consequences

**Positive:**
- Closes command execution and the auth-bypass vector with no new
  component and no `authMethod` change.
- Reuses an already-tested, already-understood authorization path.

**Negative:**
- Every MediaMTX control-API call now costs one MediaMTX→
  StreamDistribution round trip (negligible for a string compare, but
  real latency added to a previously-synchronous local check).
- The shared secret is a new piece of configuration to provision per
  environment (an Aspire parameter in dev; a secret store entry in
  prod, once prod exists).
- Production isolation of port 9997 remains unbuilt — this ADR closes
  the application-level hole, not the network-level one.

## Deployment requirement (recorded for the future k3s spec)

The SFU's control API (port 9997) must not be reachable from anything
other than the StreamDistribution service in any deployment. Whichever
spec builds the k3s/Helm topology for the SFU must either bind the API
to an internal-only interface or enforce this via NetworkPolicy/service
mesh — and must re-verify that constraint holds under `hostNetwork` if
that's how the SFU is ultimately scheduled, since standard NetworkPolicy
does not apply to host-networked pods on most CNIs.
