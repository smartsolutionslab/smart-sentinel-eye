# Mosquitto Helm fragment — per-fab MQTT broker (spec 006, ADR-0095)

This directory holds a **values sketch** for the Mosquitto broker that
EventIngestion subscribes to in production — **not a working Helm
chart**. There is no `Chart.yaml`, and `statefulset.yaml`,
`service.yaml`, `configmap.yaml` and `secret.yaml` do not exist in this
directory (they were drafted in prose below, never written). `helm
install` cannot consume this directory as-is.

Dev composes Mosquitto via the Aspire AppHost
(`src/AppHost/AppHost.cs` + `src/AppHost/mosquitto/*`), which is the
only environment this broker currently runs in. The k3s/Helm deploy
path is tracked by ADR-0024/0025 and is **not yet built** — see
issue #1015, which carries the ungenerated Helm charts (ADR-0130). The
Aspire k8s publisher (`aspire publish --target k8s`) has never been
run against this AppHost, so treat any claim that it would consume
this fragment as unverified.

## What's actually here

- **`values.yaml`** — a sketch of the values a real chart would need:
  image, persistent volume size, TLS cert source, and references to a
  Secret + ConfigMap carrying the password + ACL files. It is not
  wired to any chart templates.

## What a real chart would still need (not started)

- **`Chart.yaml`** plus the manifest templates the values above imply:
  a single-replica StatefulSet (no clustering in v1 per ADR-0095) with
  a PVC for `/mosquitto/data`; a ClusterIP Service for 1883
  (plaintext, dev network only) and 8883 (TLS); a ConfigMap wrapping
  `mosquitto.conf` + `acl.txt`; a Secret wrapping `passwords.txt`
  (PBKDF2-SHA-512 hashes produced by `mosquitto_passwd`).
- Wiring into a real Ingress/TLS story — see #1015, which also tracks
  the API gateway's edge TLS and is the natural place for this to land
  alongside it.

## The image

`values.yaml` used to pin `eclipse-mosquitto:2.0`, the stock upstream
image. That image cannot run this broker: `mosquitto.conf` requires
`plugin /mosquitto/sse-jwt-auth.so` (ADR-0100's JWT/JWKS auth plugin,
built from source in `src/AppHost/mosquitto/Dockerfile`), and stock
Mosquitto 2.0 has no such plugin and refuses to boot with a missing
plugin file. Removing the `plugin` line instead would boot the broker
with no JWT auth at all, silently defeating ADR-0100.

The correct image is the one built from
`src/AppHost/mosquitto/Dockerfile` (Mosquitto compiled from source on
Debian/glibc, plus the Go auth plugin — see that Dockerfile's header
comment for why). **No CI workflow currently builds or pushes that
image to any registry** — grepped `.github/workflows/` for
`mosquitto`, `docker build`, `docker push`, `docker/build-push-action`
and found nothing. The AppHost only ever builds it locally via
`AddDockerfile` for the dev stack (ADR-0100 addendum, 2026-05-31).
`values.yaml`'s `image.repository` is a placeholder for that reason —
see the comment there for what has to exist before it is usable.

## Operational notes (drafted for whenever the chart is built)

1. **Password rotation.** Rotate by regenerating the password file
   via `mosquitto_passwd -b passwords.txt <user> <new-password>`,
   then `kubectl create secret generic mosquitto-passwords --from-file=passwords.txt -o yaml --dry-run=client | kubectl apply -f -`.
   Mosquitto reloads on `kill -HUP`.
2. **TLS certs.** Production deploys would mount a cert + key from a
   shared TLS Secret. Cert renewal would live in the platform's
   cert-manager Issuer; mosquitto's `tls` listener would restart on
   Secret change via a StatefulSet rolling-restart.
3. **Health probe.** `mosquitto_sub -p 1883 -t '$SYS/#' -C 1 -W 5`
   is the intended readiness probe; `tcpSocket` for liveness so a
   stuck process is killed without spamming `$SYS` traffic.

## What this does NOT cover (v1, and unbuilt regardless)

- **Clustering.** Single replica per fab. If a fab outgrows
  10k connections we'll switch to EMQX per ADR-0095's rejected
  alternative.
- **Multi-fab federation.** Each fab has its own broker; cross-fab
  event flow is out of scope (spec 006 §Out of Scope).
- **Automatic password seeding.** First-time deploy would require
  `mosquitto_passwd` to be run by the operator.

## Status

This is a values sketch only. Turning it into a working chart
(`Chart.yaml` + templates), publishing the image built from
`src/AppHost/mosquitto/Dockerfile` to a registry, and wiring TLS/edge
routing are all open work tracked by issue #1015 (ADR-0024/0025,
ADR-0130).
