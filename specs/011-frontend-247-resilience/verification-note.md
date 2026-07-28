# Phase-5 verification note — 011 Frontend 24/7 Resilience (T027)

**Branch:** `011-frontend-247-resilience` · **Protocol:** [quickstart.md](./quickstart.md) §§1–5
**Runs:** §5 and §2-badge on 2026-07-27; §§1, 3, 4 on 2026-07-28.

**Outcome: §§1–5 all executed.** SC-001, SC-004 (badge leg), SC-006 and FR-017
pass on observed behaviour. SC-002 lands exactly on its 10 s budget. Two
protocol/implementation mismatches were found (§3.3 wording, §4.1 missing dev
trigger) and one criterion could not be exercised (silent renewal, FR-011).
Details and residual gaps below.

## Environment

`aspire run` against the full local stack; migrations Finished, all services
Healthy. Four cameras, four overlays, one Published 4-tile layout
(`rolling-mill-wall`). Video baseline confirmed before each section — all four
`cam-*` paths `ready:true`, `tracks:["H264"]`.

> **Baseline caveat.** MediaMTX paths were provisioned by hand, replicating
> `CameraSimProvisioner` and `MediaMtxRtspGateway.AddPathAsync`, because
> [#1121](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1121)
> crashes the simulator on any already-seeded stack and
> [#197](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/197)
> loses the paths on every MediaMTX restart. §1 therefore measures the frontend
> against a hand-built baseline.

§§1, 3, 4 were driven through Playwright rather than by hand: it authenticates
itself, and being Node it can stop/start resources mid-run, so the timings below
are measured rather than eyeballed. `resilienceLog.ts` documents the
`[resilience]` shape as an observable contract asserted by Playwright, so this
matches the intended mechanism.

## Results

| § | Criterion | Result |
|---|---|---|
| 1 | **SC-001** recovery ≤ 60 s, no reload | **PASS — 1.0 s** |
| 1 | **SC-002** never "Live" > 10 s when dead | **PASS at the boundary — 10.1 s** (1 s sampling) |
| 1 | retries never give up (≥ 2 min outage) | **PASS** — all 4 tiles still retrying at 125 s |
| 1 | SC-005 jitter sanity | **PASS** — transitions spread over ~1.9 s, not synchronised |
| 2 | **SC-004** degraded badge + unbounded retry | **PASS** — `kiosk-live-updates.spec.ts` |
| 2 | SC-004 reconnect reconciliation | **not run** |
| 3 | wall survives token expiry (60 s ×2) | **PASS** — grid + 4 tiles continuously to 171 s |
| 3 | **FR-011** silent renew logged | **NOT EXERCISED** — see below |
| 3 | expiry detected, deep link kept, no loop | **PASS** — `expired→redirecting` with `returnTo` |
| 3 | dedicated session-expired screen | **MISMATCH** — see below |
| 4 | **SC-006** kiosk reload ≤ 30 s, same layout | **PASS — 5.7 s** |
| 4 | crash-loop ladder 5 → 15 → 60 s | **PASS — 5.7 / 15.5 / 60.8 s** |
| 4 | management bounded panel | **CANNOT RUN** — see below |
| 5 | automated checks | **PASS** |

### §1 — stream recovery

Stopping `mediamtx` with four tiles live:

```
t+0.0s  ["LIVE","LIVE","LIVE","LIVE"]
t+8.1s  ["LIVE","LIVE","LIVE","LIVE"]
t+10.1s ["Reconnecting…","Connecting…","Reconnecting…","Connecting…"]   <- all left Live
```

Console, three tiles at `:23.048` and the fourth at `:23.972`:

```
[resilience] {subsystem: stream, transition: live→reconnecting, cameraIdentifier: …}
[resilience] {subsystem: stream, transition: reconnecting→connecting, cameraIdentifier: …}
```

After 125 s of outage all four were still cycling — retries never gave up.
Restart + re-provision recovered **all four tiles in 1.0 s with no page reload**
(the Playwright console listeners survived, which a reload would have severed).

**SC-002 is met but with no margin.** Measured 10.1 s against a ≤ 10 s budget at
1 s sampling, so the true value is 9.1–10.1 s. Tiles showed a frozen frame
labelled Live for ~10 s. If that budget is meant strictly, this needs a finer
measurement and probably a tighter disconnect grace
(`useWhepSession.ts` `DISCONNECT_GRACE_MS = 5_000`).

### §3 — session survival

Realm temporarily set to `accessTokenLifespan=60`, `ssoSessionMaxLifespan=180`,
`ssoSessionIdleTimeout=180`; **originals (3600 / 36000 / 1800) restored
afterwards and verified**.

The wall stayed up continuously (grid + 4 tiles) from t+5 s to t+171 s, spanning
two full 60 s token lifetimes — no visual interruption. At t+180 s exactly:

```
[resilience] {subsystem: session, transition: expired→redirecting, returnTo: /layouts/019fa58c-…}
NAV https://…/realms/smart-sentinel-eye/protocol/openid-connect/auth?client_id=smart-sentinel-eye-kiosk…
```

The deep link is preserved in `returnTo`, and there was **no redirect loop** —
one redirect, then stable for the remaining 2 minutes.

**FR-011 silent renewal was not exercised.** No `session renewing→authenticated`
line appeared, because nothing triggered a 401 in that window: the tiles were
already live and the hub already connected, so no authenticated request was
made. The wall survived because it needed no fresh token, *not* because renewal
was proven to work. Exercising it needs a forced 401 mid-flight (quickstart
§3.5), which was not run.

**§3.3 wording does not match the implementation.** The quickstart expects the
dedicated full-screen session-expired state when interaction is required. In
practice `useSessionExpiry` calls `auth.signinRedirect()`, so Keycloak presents
*its own* login form and waits. The app's "Session expired" screen
(`App.tsx:33`) is reached only via `expired→final`, which requires the redirect
to bounce back **still unauthenticated** inside the 60 s guard window — a
different scenario. Landing on Keycloak's form is defensible for a kiosk; the
quickstart should be reworded, or the flow changed to use `prompt=none` first.
Note the app's own plain sign-in-button screen was never shown under a torn-down
wall, which is what FR-014 actually forbids.

### §4 — crash containment

| Crash | Recovered | Scheduled delay | sessionStorage |
|---|---|---|---|
| #1 | 5.7 s | `delayMs: 5000, count: 1` | `count=1` |
| #2 | 15.5 s | `delayMs: 15000, count: 2` | `count=2` |
| #3 | 60.8 s | `delayMs: 60000, count: 3` | `count=3` |

Every reload returned to the **same layout URL** with `?crash=render` stripped,
so the trigger cannot survive a reload and hot-loop. The ladder matches
`RELOAD_DELAY_LADDER_MS = [5_000, 15_000, 60_000]` exactly. SC-006's ≤ 30 s
applies to the first crash (5.7 s); the longer waits are the intended crash-loop
brake.

**§4.1 cannot be run as written.** `DevCrashTrigger` exists only in
`apps/kiosk-web/`; management-web ignores `?crash=render` and rendered normally.
The boundary itself *is* correctly wired (`App.tsx:121–134`): keyed on `view`,
nav deliberately outside it so the shell survives (FR-016), `onError` logging,
and a `CrashPanel` fallback with a working reset. So T023 shipped — it simply has
no end-to-end trigger, and its behaviour is covered only by
`ErrorBoundary.test.tsx`. Either add the trigger to management or drop §4.1 from
the protocol.

### §5 — automated checks

| Check | Result |
|---|---|
| `pnpm typecheck` | 3/3 projects |
| `pnpm lint` (`--max-warnings 0`) | 3/3 projects |
| unit — shared / kiosk-web / management-web | 49 / 35 / 61 tests |
| `pnpm test:e2e` | 11/11, 25.0 s |

## Residual gaps

1. **§2 steps 3–7** — reconnect reconciliation (a variable changed while
   disconnected showing its new value; an overlay archived while disconnected
   rendering "Overlay unavailable"). Still the largest untested part of SC-004.
2. **FR-011 silent renew** — needs the §3.5 forced-401 case.
3. **§4.1 management crash** — blocked on the missing dev trigger.
4. **SC-003's full 72 h soak** — pilot-rig scope, not a dev-loop check.

## Defects found while running

- [#1121](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1121) —
  `scenario-simulator` 403s on read-back and dies on every already-seeded boot.
- [#197](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/197) —
  MediaMTX paths lost on restart; makes §1 unrunnable without manual
  re-provisioning after each restart.
- The `/hubs` proxy defect fixed on this branch: hyphenated Aspire
  service-discovery keys are dropped by POSIX shells, so the dev proxy silently
  did not exist on Linux.
