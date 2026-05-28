# Mosquitto Helm fragment — per-fab MQTT broker (spec 006, ADR-0095)

This directory holds the **prod-side** Helm overlay for the
Mosquitto broker that EventIngestion subscribes to in production.
Dev still composes Mosquitto via the Aspire AppHost (`src/AppHost/AppHost.cs`
+ `src/AppHost/mosquitto/*`); this fragment is what `aspire publish --target k8s`
will eventually consume.

## Files

- **`values.yaml`** — per-fab values: image tag, persistent volume
  size, TLS cert source, and a reference to the Secret + ConfigMap
  that carry the password + ACL files.
- **`statefulset.yaml`** — single-replica StatefulSet (no clustering
  in v1 per ADR-0095) with a PVC for `/mosquitto/data`. Mounts the
  password / ACL ConfigMap at `/mosquitto/config`.
- **`service.yaml`** — ClusterIP exposing port 1883 (plaintext, dev
  network only) + 8883 (TLS for prod external traffic).
- **`configmap.yaml`** — wraps `mosquitto.conf` + `acl.txt`.
- **`secret.yaml`** — wraps `passwords.txt` (PBKDF2-SHA-512 hashes
  produced by `mosquitto_passwd`).

## Operational notes

1. **Password rotation.** Rotate by regenerating the password file
   via `mosquitto_passwd -b passwords.txt <user> <new-password>`,
   then `kubectl create secret generic mosquitto-passwords --from-file=passwords.txt -o yaml --dry-run=client | kubectl apply -f -`.
   Mosquitto reloads on `kill -HUP`.
2. **TLS certs.** Production deploys mount a cert + key from a
   shared TLS Secret. Cert renewal lives in the platform's
   cert-manager Issuer; mosquitto's `tls` listener is restarted by
   the StatefulSet rolling-restart on Secret change.
3. **Health probe.** `mosquitto_sub -p 1883 -t '$SYS/#' -C 1 -W 5`
   is the readiness probe; we use `tcpSocket` for liveness so a
   stuck process is killed without spamming `$SYS` traffic.

## What this fragment does NOT do (v1)

- **Clustering.** Single replica per fab. If a fab outgrows
  10k connections we'll switch to EMQX per ADR-0095's rejected
  alternative.
- **Multi-fab federation.** Each fab has its own broker; cross-fab
  event flow is out of scope (spec 006 §Out of Scope).
- **Automatic password seeding.** First-time deploy requires
  `mosquitto_passwd` to be run by the operator. Spec 006 PR A's
  `src/AppHost/mosquitto/passwords.txt` ships a placeholder with
  inline regeneration instructions.

## Status

The values + manifest scaffolding lives here as the canonical
Helm contract; full wiring into the prod CI/CD pipeline is its own
operational PR. Spec 006 ships the runtime; the production deploy
path will be exercised when the first fab spins up.
