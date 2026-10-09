# Spec 323 — The dot inside a device name

**Issue:** [#2627](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2627)
— *Device identifier grammar is unbounded beyond non-empty (`plc--`, `plc-.` accepted)*.
Filed from spec 264 (issue #2575) plan §6 item 2. Picked up by the autonomous lane
(ADR-0144); #2627 is the feature-level tracking issue — no new issue, no per-task issues.

**Spec number.** Checked 2026-10-09 against `origin/develop` (highest: 321), every remote and
local branch (`git ls-tree --name-only <ref> specs/`), and every `D:\Github\sse-*` worktree's
working tree. 322 is claimed by the unmerged branch `fix/2502-standardize-variable-audit-identifier`
(`specs/322-one-variable-one-timeline`). This spec is **323**. Re-check before the PR is opened.

**ADRs referenced:**

- **ADR-0038** — hand-written value objects. `ClientId`'s grammar is *not* changed (plan §2).
- **ADR-0047 / ADR-0089** — `Result<T, Error>` / `ApiError`; the refusal reuses the existing
  `RegisterDeviceError.InvalidDeviceIdentifier` (400 `DEVICE_INVALID_IDENTIFIER`).
- **ADR-0142** — `POST /devices/register` is a keyed create; a refusal is
  `IdempotentOutcome.NothingCreated`. Unchanged.
- **ADR-0036** — smallest change: one guard in one handler.
- **ADR-0139 / ADR-0144** — behaviour-changing, so phase 4a is **red**.

**No new ADR.** This narrows one input's accepted grammar per a requirements decision already made
on the issue. No new pattern, no new type.

## Decision (recorded, not reopened)

The authoritative decision is the user's comment on #2627 of **2026-10-08**:

> device identifier grammar requires alphanumeric segments only — each hyphen-separated segment
> alphanumeric, no empty segments, no leading/trailing punctuation (rejects 'plc--', 'plc-.', etc.).

The 2026-10-09 comment (start with a letter or digit, punctuation allowed after) is **superseded**;
the correction comment of 2026-10-09 16:07 UTC says so explicitly. A first draft of this spec
followed the superseded comment and was discarded before commit.

**Reading of the decision.** "Device identifier" is the `deviceIdentifier` field — the part after
`<deviceType>-`. The rule applies to **every** hyphen-separated segment of it, not just the first.
Because both device types (`plc`, `inference`) are themselves alphanumeric, this is equivalent to
saying every hyphen-separated segment of the composed device clientId is alphanumeric. Grammar:

```
deviceIdentifier := segment ( "-" segment )*
segment          := alnum+
```

So `.` and `_` are rejected anywhere; `-` is allowed only as a separator between two non-empty
segments (no leading, trailing or doubled hyphen).

**[ASSUMPTION] "alphanumeric" = `char.IsLetterOrDigit`**, the predicate `ClientId.IsValid` already
uses for "letter or digit" — Unicode letters and digits included, as `ClientId` accepts them today.
The decision does not say ASCII-only; choosing the existing predicate avoids a second, narrower
meaning of "letter" in the same context. If ASCII-only is wanted, it is a one-predicate change and
the test rows below gain one non-ASCII row; flag it at review rather than block.

## Context

`RegisterDeviceCommandHandler` composes `ClientId.From($"{deviceType}-{deviceIdentifier}")`.
`ClientId` allows letters, digits, `.`, `_`, `-` anywhere after a leading letter/digit, and the
composed string always starts with `plc`/`inference`. So `"-"`, `"."`, `"x.y"`, `"x_y"`, `"x-"`,
`"x--y"` all register a real Keycloak client today. Spec 264 closed only null/empty/whitespace.

## User Story 1 (P1) — A device identifier is hyphen-separated alphanumeric segments

As an administrator registering a device, I want an identifier with punctuation inside a segment or
an empty segment refused, so that every device clientId has a regular `<type>-<seg>(-<seg>)*` shape
the FR-008 MQTT ACL can bind to.

### Acceptance scenarios

```gherkin
Scenario Outline: Identifier with an empty segment or non-alphanumeric character is refused (bad request)
  Given an administrator with the device-registration scope and access to fab "munich"
  When they POST /devices/register?fabId=munich with deviceType "<type>" and deviceIdentifier "<id>"
  Then the response is 400 with problem title "DEVICE_INVALID_IDENTIFIER"
  And the detail is "deviceIdentifier rejected: each hyphen-separated segment must be one or more letters or digits."
  And no Keycloak client is created and no registered-client row is written
  Examples:
    | type      | id            |
    | plc       | -             |
    | plc       | .             |
    | plc       | -x            |
    | plc       | x-            |
    | plc       | x--y          |
    | plc       | x.y           |
    | plc       | x_y           |
    | plc       | line-3.cell_2 |
    | plc       |  x            |
    | inference | a_b           |

Scenario Outline: Hyphen-separated alphanumeric identifiers are accepted (happy path)
  When they register deviceType "plc" with deviceIdentifier "<id>"
  Then the response is 201 with clientId "plc-<id>"
  Examples:
    | id            |
    | x             |
    | 4station      |
    | station-4     |
    | line-3-cell-2 |

Scenario: Refusal order is unchanged
  Given deviceType "manual" and deviceIdentifier "x.y"
  Then the response is 400 "DEVICE_INVALID_TYPE" (type is checked first)

Scenario: Duplicate still conflicts (conflict)
  Given "plc-station-4" is already registered
  When it is registered again
  Then the response is 409 "DEVICE_ALREADY_REGISTERED"   # existing behaviour, already pinned

Scenario: Unauthorised caller (auth)
  Given a caller without the device-registration scope, or without access to fab "munich"
  Then the response is 403 before any identifier check   # existing behaviour, already pinned
```

### Functional requirements

- **FR-001** A device registration is refused with `InvalidDeviceIdentifier` (400
  `DEVICE_INVALID_IDENTIFIER`) unless `deviceIdentifier`, split on `-`, yields only non-empty
  segments whose every character satisfies `char.IsLetterOrDigit`.
- **FR-002** The check runs after the device-type check and spec 264's empty check, before
  `ClientId.From`, Keycloak, or the repository. Refusal order is otherwise unchanged.
- **FR-003** The 255-character limit stays with `ClientId` on the composed string (unchanged).
- **FR-004** `ClientId`'s grammar is unchanged: already-persisted clientIds such as `plc--` or
  `plc-a.b` still load and can still be disabled/rotated by clientId; kiosk and webhook clientIds
  keep `.`/`_`.

### Out of scope

- Existing non-conforming device rows/clients in any environment (no migration; FR-004).
- Kiosk and webhook clientIds (different composition paths, own grammar).
- A `DeviceIdentifier` value object (plan §2).

## Independent end-to-end test

1. Boot the stack; mint an admin token for `identity`.
2. `POST /devices/register?fabId=munich` with `{"deviceType":"plc","deviceIdentifier":"x.y"}` →
   expect 400, title `DEVICE_INVALID_IDENTIFIER`. Before the fix: 201 with clientId `plc-x.y`.
3. Same with `"-"` → 400 (before: 201 `plc--`).
4. A unique `"e2e-<guid:N>"` → 201; then disable it to clean up.

## Latency budget impact

**N/A.** Device registration is an administrative path, not on the event→overlay path (§IV).
