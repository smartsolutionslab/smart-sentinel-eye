# Spec 439 — The wall that knows its own credential

**Issue:** #2781 · **ADRs:** ADR-0131 (governing: the kiosk keeps its own grant; amends ADR-0080), ADR-0080,
ADR-0146 (calm wall surface), ADR-0143 (retry is what needs justifying), ADR-0144 (lane), ADR-0117 (§IV legs)
**Specs built on:** 051 (identity-failure verdicts), 052 (`kiosk-wall` client), 205 (renewer resolves the token),
233 (WHEP refusal is terminal), #2565 (never retry)
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** red (behaviour-changing)
**Security-sensitive:** yes — it triggers a credential renewal from a stream signal; `security-reviewer` at phase 6.

## Premise, re-verified at `b5579a60` — can "renew" do anything for a kiosk credential today?

**Yes. A mechanism exists, and it is not the enrolment secret.** The browser kiosk's credential is its OIDC
session (ADR-0131), not a device credential, so #1987/#1988 (no device runtime consumes an enrolment secret —
both still OPEN) do not bear on it.

| Claim                                             | Observed                                                                                                                                                           |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| A wall display holds a refresh token              | `apps/kiosk-web/src/app/auth.ts:47-49` — `kiosk-wall` requests `openid offline_access`                                                                             |
| A renewal can be forced on demand                 | `apps/kiosk-web/src/app/useSessionExpiry.ts:178-191` registers a renewer calling `auth.signinSilent()`; resolves the new `access_token`, or `undefined` on failure |
| Renewal is single-flight already                  | `apps/shared/src/api/gateway.ts:81-95` `renewSessionOnce` (module-private today) — concurrent callers share one in-flight renewal                                  |
| A refused renewal is already classified and shown | `useSessionExpiry.ts:118-131` classifies; `App.tsx:36-38` renders spec 051's `NotAuthorizedScreen` for `refused` (`invalid_grant`, … — `identityFailure.ts:38-56`) |
| WHEP 401 vs 403 is distinguishable                | `apps/shared/src/streaming/WhepClient.ts:283-286` → `WhepError` kind `unauthorized` / `forbidden`                                                                  |
| A refused tile is terminal; a remount re-arms it  | `apps/shared/src/ui/composites/useWhepSession.ts:364-371` ("only an outside change … remount … leaves this state")                                                 |
| Tiles read the token at connect time              | `apps/kiosk-web/src/features/cell/LayoutGrid.tsx:90-96` (`accessTokenRef`) — a remount after renewal presents the new bearer                                       |
| **A WHEP 401 is not proof of a bad credential**   | `src/StreamDistribution/Api/Program.cs:31-33` — MediaMTX reports _any_ non-2xx from the authorize hook (429 included) as a flat 401                                |

So "one renew attempt" = one call to the existing single-flight renewer, then remounting the refused tiles.
Nothing new on the server, in Keycloak, or in the auth flow.

## User story 1 (P1) — a wall-wide 401 reads as the display's credential, and tries once to recover

As a fab operator looking at a wall whose every tile was refused at once, I want the wall to say it is the
display's own access that failed, try once to recover by itself, and — if that fails — tell me what to ask for.

### Rule (owner decisions 2026-10-21 / 2026-10-22, verbatim intent)

The wall-level state is entered when **all** of: (a) every currently mounted camera tile is in terminal refusal
of kind `unauthorized` (401); (b) at least **two** such tiles are mounted; (c) the latest 401 arrived no more
than **10 000 ms** after the earliest (inclusive). 403 (`forbidden`) never counts and stays per tile.
Then exactly **one** renew attempt.

- While renewing: **"This display's access was refused. Reconnecting with a new credential."**
- If it fails: **"Access refused. Ask an administrator to re-enrol this display."**

### Acceptance scenarios

```gherkin
Scenario: happy — renewal recovers the wall
  Given a wall with 2 camera tiles
  When both tiles' WHEP POST answer 401 within 10 s
  Then the wall shows "This display's access was refused. Reconnecting with a new credential."
  And the session renewer is called exactly once
  When the renewal resolves a new access token
  Then the wall-level state disappears and both tiles reconnect presenting the new bearer

Scenario: renewal returns no token because the provider is unreachable
  Given the wall-level state was entered
  When the renewer resolves no usable token (empty counts as none) and the provider did not answer
  Then the wall stays on "This display's access was refused. Reconnecting with a new credential."
  And the session machine's own retry runs, and a new access token lifts the state
  And no second renew attempt is made by the wall

Scenario: the provider answered the renewal with a user holding no credential
  Given the wall-level state was entered
  When the renewer resolves no usable token and the provider's answer was a user with no access token
  Then the wall shows "Access refused. Ask an administrator to re-enrol this display."
  And no second renew attempt is made

Scenario: conflict — the provider refuses the screen
  Given the wall-level state was entered
  When renewal fails with invalid_grant
  Then spec 051's NotAuthorizedScreen is shown (App precedence unchanged) and the wall-level state is not

Scenario: conflict — 403 stays per tile
  Given a wall with 2 tiles, one refused 401 and one refused 403
  Then no wall-level state is shown and the 403 tile shows its per-tile "Access refused" unchanged

Scenario: bad input — one tile
  Given a wall with exactly 1 camera tile refused 401
  Then no wall-level state is shown and no renewal is triggered by this feature

Scenario: outside the window
  Given 2 tiles refused 401 at t=0 ms and t=10 001 ms
  Then no wall-level state is shown   # t=10 000 ms enters it

Scenario: a tile that is not refused
  Given 3 tiles, 2 refused 401 within 10 s and 1 live (or connecting, or offline)
  Then no wall-level state is shown
  When the third tile later refuses 401 within 10 s of the first 401
  Then the wall-level state is entered

Scenario: auth — the renewal succeeded but the tiles 401 again
  Given the renewal resolved a new token and the tiles remounted
  When every tile refuses 401 again
  Then no second wall-level state and no second renewal: tiles show today's per-tile "Access refused"
```

### Assumptions (marked; the owner may overturn any at review)

- **A1 — spec 051's screen wins on a provider refusal.** `App.tsx:36` already puts refusal first; the instruction
  it gives ("re-commission this screen") is the same instruction as the second state. Changing spec 051's wording
  is out of scope.
- **A2 — corrected after review: no token is not a refusal.** A renewer that resolves no token also covers an
  unreachable provider (`gateway.ts` swallows the rejection), which `useSessionExpiry` classifies as recoverable
  and keeps retrying; telling someone to re-enrol would be wrong. The wall therefore keeps the first state, and
  shows the second only when the provider answered with a user holding no access token
  (`apps/kiosk-web/src/app/renewalAnswer.ts`, kiosk-local, set by the registered renewer). A genuine `invalid_grant`
  ends on `NotAuthorizedScreen` (A1) and never reaches the second state. A renewal that _succeeds_ followed by
  fresh 401s is the authorize hook, not the credential (`Program.cs:31-33`): the tiles fall back to today's
  per-tile behaviour. One attempt per episode; an episode ends when a tile is live again.
  A renewed token remounts the tiles only once it has reached the access token they read their bearer from.
- **A3 — the second state clears if the session recovers by itself** (spec 051's recoverable retry loop
  publishes a new `access_token`): the tiles remount once. That is the existing session machine, not a second
  renew attempt by this feature.
- **A4 — "mounted tile"** = a grid cell with a camera (`LayoutGrid.tsx:241-243`); empty cells do not count.
  A tile that unmounts, reconnects or goes live is forgotten (its 401 leaves the set).

## Independent end-to-end test

`e2e/kiosk-credential-refused.spec.ts` on the seeded 9-tile live wall: route every WHEP POST to 401 → first-state text visible;
token endpoint fulfilled → tiles reach Live; variant with token endpoint answering 400 `invalid_grant` →
`identity-not-authorized`; variant aborting the token endpoint → the first-state text stays and the second never appears. The token-endpoint
route is installed after the sign-in (its code exchange posts to the same endpoint). Each test counts its own
interceptions (the spec 051 pattern) and asserts `wall-credential→renewing` was logged exactly once.

## Constraints

- **ADR-0146:** dark surface tokens and muted text only; no bright fill larger than a status chip.
- **No new motion:** no `animate-*`/`transition-*` on the new element; `MotionLanguageTests` must stay green
  unmodified.
- **Retry discipline (ADR-0143, #2565):** one attempt per episode, latched; tiles never re-enter a retry ladder on 401.

## Latency budget (constitution §IV)

Leg: **Overlay composite + render (≤ 50 ms)** — the new element sits in the wall render tree. No per-frame work:
state changes only on a tile refusal. Event → overlay state, SFU, decode and presentation buffer: N/A.
Measured per the render-leg procedure (plan §6).

## Out of scope

Server-side error code (option C, deferred); #1976 (unattended reboot); #1987/#1988; spec 051's wording.
