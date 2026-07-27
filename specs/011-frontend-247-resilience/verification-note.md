# Phase-5 verification note — 011 Frontend 24/7 Resilience (T027)

**Date:** 2026-07-27 · **Branch:** `011-frontend-247-resilience` · **Protocol:** [quickstart.md](./quickstart.md) §§1–5

**Outcome: PARTIAL.** §5 passed in full and the §2 degraded-badge leg passed
via its e2e proxy. §§1, 3, 4 and the remainder of §2 were **not** executed —
they require an interactive kiosk session, which could not be established (see
*Why §§1–4 were not run*). T027 remains open.

## Environment

`aspire run` against the full local stack. `migrations` reached **Finished**;
Postgres, Keycloak, RabbitMQ, MediaMTX, MinIO, Mosquitto and all nine services
reported **Healthy**. Seeded data present from a prior run: 4 cameras, 4
streams, 4 overlays (4 revisions), 1 layout with a **Published** revision and 4
tiles.

Video baseline confirmed live before testing — all four `cam-*` paths on the
main MediaMTX reported `ready:true`, `tracks:["H264"]`, ~5 MB received each.

> **Caveat on the baseline.** The stream paths were **provisioned by hand** for
> this run, replicating `CameraSimProvisioner` (loop paths on `camera-sim`) and
> `MediaMtxRtspGateway.AddPathAsync` (source paths on `mediamtx`). They were not
> produced by the normal path, because of the two defects below. Any future §1
> result obtained this way is measuring the frontend against a hand-built
> baseline, not against the system's own provisioning.

## Results

| § | Criteria | Result | Evidence |
|---|---|---|---|
| 1 | SC-001, SC-002 — stream recovery | **not run** | requires interactive kiosk |
| 2 | SC-004 — degraded badge + unbounded retry (FR-006/007) | **PASS** | `e2e/kiosk-live-updates.spec.ts` |
| 2 | SC-004 — remainder (variable change while down, archived overlay → "Overlay unavailable", pre-mount case) | **not run** | requires interactive kiosk |
| 3 | SC-003 leg — session survival | **not run** | requires interactive kiosk + realm edits |
| 4 | SC-006 — crash containment | **not run** | requires interactive kiosk |
| 5 | Automated checks | **PASS** | below |

### §5 — automated checks (all green)

| Check | Result |
|---|---|
| `pnpm typecheck` | 3/3 projects |
| `pnpm lint` (`eslint --max-warnings 0`) | 3/3 projects |
| `pnpm test` — `apps/shared` | 8 files, **49 tests** |
| `pnpm test` — `apps/kiosk-web` | 5 files, **35 tests** |
| `pnpm test` — `apps/management-web` | 13 files, **61 tests** |
| `pnpm test:e2e` (chromium + kiosk) | **11/11 passed**, 25.0 s |

### §2 — what the e2e proxy actually proves

`kiosk shows the degraded badge while the hub is unreachable and clears it
after recovery` (passed, 4.5–7.6 s over two runs):

1. Signs in to the kiosk as the seeded `operator` and reaches the picker.
2. Aborts **every** `/hubs/**` request (negotiate + transport) and reloads, so
   even the *initial* connect fails — this is the FR-006 case that initial-connect
   failures must retry indefinitely rather than give up.
3. Asserts `live-updates-degraded` becomes visible.
4. Restores the network and asserts the badge is hidden within 45 s, covering
   the retry ladder's 30 s ±20 % jitter ceiling.

This is a genuine test of the badge and the unbounded-retry recovery. It does
**not** cover the reconnect *reconciliation* half of SC-004 — that a variable
changed while the hub was down shows its new value, and that an overlay archived
while down renders "Overlay unavailable" — which is the part §2 steps 3–5 exist
to check.

## Why §§1–4 were not run

The protocol needs a browser signed into the kiosk with devtools open. The
Chrome instance attached to automation opened its tab in a **background
window**, and sign-in attempts kept landing in a different, visible window.
Confirmed directly on the automation tab: `visibility: "hidden"`,
`focused: false`, username field empty, OIDC `state` unchanged across attempts.
Only one browser was paired, so there was no other session to switch to.

Nothing about this indicates a product defect — it is a harness limitation.
The remaining legs need either that window brought to the foreground, or a
manual pass.

## Defects found while preparing the stack

Both are **pre-existing and unrelated to spec 011**, but both block a clean
first-boot run of this protocol.

1. **`scenario-simulator` crashes on every boot — no cameras or loop paths get
   provisioned.** It `POST`s an overlay → `409` (already seeded from a prior
   run), falls back to `GET /overlays` → **403**, because its token carries only
   `*.write` scopes. The exception is unhandled and
   `BackgroundServiceExceptionBehavior=StopHost` takes the whole worker down.
   Fix direction: treat the `409` as success instead of reading back.

2. **MediaMTX paths do not survive a restart.** Both MediaMTX instances came up
   with zero paths and nothing restored them — this is open tech-debt
   [#197](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/197)
   (`SourceUrl` is not persisted on the `Stream` aggregate, so the reconciler
   cannot re-add missing paths). This also means **§1 cannot be run as written**
   without re-provisioning by hand after each `mediamtx` restart: stopping and
   restarting the resource destroys its paths, so the stream could never return
   and the kiosk would appear to fail SC-001 for a reason that has nothing to do
   with the frontend.

## To finish T027

1. Bring the automation Chrome window to the foreground (or run manually) and
   execute §§1, 3, 4 plus §2 steps 3–7.
2. For §1, re-provision the four `cam-*` source paths immediately after each
   `mediamtx` restart until #197 is fixed.
3. For §3, record the realm's current *SSO Session Max* and *Access Token
   Lifespan*, set them to ≈3 min / ≈1 min, run the section, then restore the
   originals — the `keycloak-data` volume is sticky, so the change otherwise
   persists.
4. SC-003's full 72-hour criterion is a pilot-rig soak and is out of scope for
   the dev loop; the dev proxy is §§1–3 in one session with no manual reload.
