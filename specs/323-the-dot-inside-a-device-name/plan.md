# Plan 323 — The dot inside a device name

**Spec**: [spec.md](spec.md) · **Issue**: #2627 · **Phase**: 2 (Plan)

## 1. Where it lives

Bounded context **Identity**, layer **Application**:
`src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs`. One production file.

Today's sequence in `HandleAsync`: device-type check → empty check (spec 264) →
`ClientId.From($"{deviceType}-{deviceIdentifier}")` (catch → `InvalidDeviceIdentifier`) → duplicate
→ Keycloak → persist. The identifier is **never validated on its own** — only as part of the
composed `ClientId`. There is no identifier type; spec 264 (#2575) added an inline
`string.IsNullOrWhiteSpace` early return in this handler. That is the check to extend.

## 2. Design: a composition-time guard, not a new VO and not a `ClientId` change

Directly after the empty check:

```csharp
if (!IsHyphenSeparatedAlphanumeric(deviceIdentifier))
{
    return Failure(RegisterDeviceFailures.InvalidDeviceIdentifier(
        "each hyphen-separated segment must be one or more letters or digits."));
}

private static bool IsHyphenSeparatedAlphanumeric(string deviceIdentifier) =>
    deviceIdentifier.Split('-').All(segment => segment.Length > 0 && segment.All(char.IsLetterOrDigit));
```

(Illustrative; the engineer may shape it, but the predicate and message are fixed by the tests.)

**Does the stricter grammar interact with `ClientId` differently?** Yes, in one way, and it
confirms the location rather than moving it: the new rule is **narrower than `ClientId`'s** for
device ids (no `.`/`_` at all), whereas the superseded rule was merely `ClientId`'s rule re-applied
to a suffix. That means the rule cannot live in `ClientId`:

- `ClientId` is shared with kiosk clients (`ClientId.From(body.ClientId)` in `KiosksEndpoints`) and
  webhook clients (`webhook-{integrationName}`), whose grammar legitimately allows `.`/`_`.
- `ClientId` is the EF conversion for every persisted row (`RegisteredClientConfiguration`) and the
  parser for `DELETE /devices/{clientId}`. Narrowing it would make existing `plc--`/`plc-a.b` rows
  fail to materialise and become undisableable (FR-004).
- `ClientId` has no notion of which kind of client it names; the device-specific shape is FR-008's
  concern, and `ClientId`'s own doc comment already says that shape "is checked at command-time,
  not by this VO". This plan does exactly that.

After the new guard, `ClientId.From` can only fail on length (> 255 composed). The existing
`try/catch` stays — it is still the length refusal — and is not touched.

**No `DeviceIdentifier` VO.** §II binds domain-model state; the domain never holds the identifier
alone. A VO would ripple through `RegisterDeviceCommand`, `DeviceCredentialsDto`, the log message,
`ClientRegisteredDomainEventHandler.SplitDeviceClientId` and the replay path — a refactor inside a
bug fix (ADR-0036). Spec 264 plan §2 declined it on identical grounds.

**Predicate:** `char.IsLetterOrDigit`, matching `ClientId` (spec [ASSUMPTION]).

**Existing callers checked.** Every in-repo registration uses conforming identifiers
(`station-4`, `t040-{guid:N}`, `mqtt-audience-{guid:N}`, `orphan-{guid:N}`, …); `Guid` `:N` format is
32 hex digits. No frontend registers devices. No test or fixture needs changing.

## 3. Entities / invariants

No entity, VO or aggregate changes. New Application-level invariant: *a device clientId is
`<type>-<seg>(-<seg>)*` with every segment non-empty alphanumeric.* `ClientId` grammar and refusal
order unchanged.

## 4. Messaging / boundaries

None. No domain or integration event changes, no `Shared.Contracts` change, no cross-context
reference. `ClientRegisteredDomainEventHandler.SplitDeviceClientId` splits on the first `-` after
the type and is unaffected (the identifier may still contain `-`, as today).

## 5. Tests (phase 4a: **red**)

**Unit** — `tests/Identity.Application.Tests/Commands/RegisterDeviceCommandHandlerTests.cs`
(existing class; no shard-filter change needed).

New theory `An_identifier_that_is_not_hyphen_separated_alphanumeric_segments_returns_InvalidDeviceIdentifier(string deviceType, string deviceIdentifier)`,
rows: (`plc`,`-`), (`plc`,`.`), (`plc`,`-x`), (`plc`,`x-`), (`plc`,`x--y`), (`plc`,`x.y`),
(`plc`,`x_y`), (`plc`,`line-3.cell_2`), (`plc`,`" x"`), (`inference`,`a_b`). Assert: `IsSuccess`
false; error is `RegisterDeviceError.InvalidDeviceIdentifier` with
`Reason == "each hyphen-separated segment must be one or more letters or digits."` (distinguishes
the new refusal from the composed-grammar one, so `" x"` cannot pass for the wrong reason);
`repo.Clients` empty; `keycloak.Created` empty.

Expected red: rows `-`, `.`, `-x`, `x-`, `x--y`, `x.y`, `x_y`, `line-3.cell_2`, `a_b` return
**success** today; row `" x"` fails today with the **ClientId** reason (wrong `Reason`) — red on the
`Reason` assertion. Both are red for the right reason.

Green-guard theory `A_hyphen_separated_alphanumeric_identifier_registers(string deviceIdentifier)`,
rows `x`, `4station`, `station-4`, `line-3-cell-2`. Assert success and
`ClientId == "plc-" + deviceIdentifier`. Green before and after.

Existing tests are characterisation and must pass **unmodified**: happy path, duplicate, type,
`Invalid_device_identifier_returns_InvalidDeviceIdentifier` (`"has space"` — now refused by the new
guard instead of `ClientId`; it asserts the type only, so it stays green unmodified), absent
identifier, Keycloak failure.

**No new integration test.** The refusal reuses an error already proven to reach the wire as 400
`DEVICE_INVALID_IDENTIFIER` by `AbsentDeviceIdentifierIsRefusedIntegrationTests` (spec 264); there
is no JSON-binding subtlety here the unit test cannot see. The existing Identity integration tests
all register conforming identifiers and act as the over-refusal guard on the real stack when CI
runs them. Phase 5 observes the refusal end to end (spec §Independent end-to-end test).

## 6. Risks / follow-ups

- Environments may hold non-conforming device clients (`plc--`, `plc-a.b`). Untouched by design
  (FR-004); not filed — spec 264 §6 item 3 covers the analogous `plc-` rows.
- ASCII vs Unicode "alphanumeric" — spec [ASSUMPTION]; reviewer to confirm.
