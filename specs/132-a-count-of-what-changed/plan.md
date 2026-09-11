# Plan 132 — A count of what changed

**Spec:** `specs/132-a-count-of-what-changed/spec.md` · **Issue:** #2169

## The seam

Three frames, and the fact travels up all three:

```
HttpKeycloakAdminClient.StripInheritedRealmRolesAsync(realm, uuid, ct)
    knows: assigned.Length == 0  ->  nothing to remove        [computed, discarded]
IKeycloakAdminClient.StripInheritedRealmRolesAsync(clientId, ct)
    Task  ->  Task<bool>                                      [the port]
KioskPrivilegeSweep.SweepAsync(ct)
    counts true answers, guards the log on that count          [the report]
```

Nothing below the port changes what it *does*. The private overload already
returns early; it now returns `false` there and `true` after a successful
`DELETE`.

## Files

| File | Change |
|---|---|
| `src/Identity/Application/KeycloakAdmin/IKeycloakAdminClient.cs` | `Task` → `Task<bool>`, with the meaning of the answer documented where the idempotence paragraph already is |
| `src/Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs` | both overloads return the answer; the enrolment call site discards it (`_ = await …`) because a freshly-created account always holds the composite and a count there would report nothing new |
| `src/Identity/Application/KeycloakAdmin/KioskPrivilegeSweep.cs` | count the `true` answers; guard the log on that count; `KioskSweepOutcome` carries it |
| `src/Identity/Application/Log.cs` | **comment only.** `"{StrippedCount} of {KioskCount}"` already reads true once the numerator means what it says, and the field names are what an operator queries by — moving either would be a second change wearing this one's clothes |
| `tests/Identity.Infrastructure.Tests/Fakes/EnrolledKiosksKeycloakAdminClient.cs` | can express "this kiosk holds nothing" |
| `tests/Identity.Application.Tests/Fakes/FakeKeycloakAdminClient.cs` | returns whether the strip changed anything — `HashSet.Add`'s own answer, which models the provider's idempotence exactly |
| `tests/MigrationRunner.Tests/KeycloakProvisionedFabSourceTests.cs` | two stub implementations, signature only |
| `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KioskPrivilegeSweepSteadyStateTests.cs` | the two new reds |
| `specs/092-the-sweep-is-registered-or-retired/spec.md` | step 9 becomes true; the block recording it false records instead when and how it was fixed |

## `KioskSweepOutcome`

Today: `record (int KioskCount, IReadOnlyList<string> Unreachable)` with
`StrippedCount => KioskCount - Unreachable.Count` — a *derived* number that was
never the strip count. It becomes a carried component:

```
KioskSweepOutcome(int KioskCount, int StrippedCount, IReadOnlyList<string> Unreachable)
```

The three existing assertions on `StrippedCount` (`ShouldBe(2)` twice) stay true
under the new meaning, because their fakes' accounts genuinely held privileges —
which is the check that this is a meaning correction and not a behaviour change
hiding in the outcome type.

`Unreachable` stays a list of names, not a count: #2166's reason for it — "so a
caller can say which kiosk still holds the privilege" — is untouched.

## Why the port and not a second query

The sweep could ask for the account's role mappings before stripping. It would be
a second round trip per kiosk, it would race the strip, and it would put a
provider concept (`role-mappings/realm`) into Application, which is the seam the
port exists to keep closed. The value is already computed one frame down.

## Why not `int`

`Task<int>` — how many role mappings were removed — is more information than the
line needs and more than any caller asks for. The numerator counts *accounts*,
not privileges, so the extra number would exist only to be discarded. ADR-0036:
no speculative generality.

## Phase 4a colour

**Red, behaviour-changing.** When the line appears changes, and what its
numerator means changes. Characterisation would encode the defect as the safety
net — which is precisely the case CLAUDE.md names when it says a refactor that is
also a bug fix is two issues.

The reds are asserted in **both directions** in one file, because an
absence-only assertion passes over deleted logging. Eight assertions that could
not fail have surfaced in this session.

## Verification

Unit level only, per the issue: `dotnet test` over `Identity.Application.Tests`,
`Identity.Infrastructure.Tests` and `MigrationRunner.Tests`, plus
`dotnet build -c Release`. No Aspire stack — the sweep's end-to-end claim is spec
092's procedure, and this spec corrects that procedure's text rather than
re-running it. Another agent holds the stack.

## Risks

- **The port is implemented in five places** (production, three test fakes, two
  stubs in `KeycloakProvisionedFabSourceTests`). A missed one is a compile error,
  not a silent pass, so `dotnet build -c Release` is the whole guard.
- **`KioskSweepOutcome` gains a component**, so every construction site must be
  updated. There is one.
