# Plan 183 — A list that stays in its fabs

**Spec:** `specs/183-a-list-that-stays-in-its-fabs/spec.md`
**Issue:** #2281

---

## 1. Bounded context and layers

One context: **Identity**. No cross-context project reference is added; nothing
enters `Shared.Contracts`; no message is versioned. `FabResolution`,
`FabClaims`, `IFabAuthorizationGuard` and `FabAuthorizationExceptionHandler` all
live in `ServiceDefaults`, which every context's Api already references —
Identity's Api injects the guard into six handlers in these same three files
today.

| Layer | Change |
|---|---|
| **Domain** | **None.** `RegisteredClient.Fab` already exists and is already a `FabIdentifier`. No aggregate change, no new value object, no repository method. |
| **Application** | The three list queries swap `Option<FabIdentifier> Fab` for `IReadOnlyList<FabIdentifier> Fabs`; the three handlers pass it through; `RegisteredClientProjection` filters unconditionally. No new error variant. |
| **Infrastructure** | **None.** No migration — `fab` is an existing, populated, indexed column (`HasIndex(Kind, Fab, DisabledAt)`). No EF configuration change. |
| **Api** | One new internal helper file binding `FabResolution.ResolveForReadAsync` to Identity's `FabIdentifier`; the three `List` handlers call it instead of their `if/else`. |

**Nine production files, one of them new.** Enumerated in `tasks.md`.

## 2. The fix shape, and why it is one change rather than three

### What the three endpoints actually share

Investigated rather than assumed (spec §2):

```
KiosksEndpoints.List  ──┐
DevicesEndpoints.List ──┼─> ListXQuery(Option<FabIdentifier> Fab)
WebhookRotation.List  ──┘        │
                                 └─> RegisteredClientProjection.ListAsync(source, kind, fab, ct)
                                         └─> if (fab.HasValue) query = query.Where(...)   ← the hole
```

**One filter, in one method, reached by all three.** The endpoint halves are
three byte-identical copies of the same nine lines (only the error-code prefix
differs: `KIOSK_` / `DEVICE_` / `WEBHOOK_`). So this is one coherent change
touching three call sites plus one shared projection — not three parallel
efforts. Decomposing it into three would have the second and third tasks editing
the file the first one changed.

### The established pattern, read from the six adopters

`CameraEndpoints:484-517`, `StreamEndpoints:316-349` and
`EventIngestionFabResolution:72-105` are the same twenty lines three times, and
`EventIngestion`'s doc comment states why it was lifted into its own file rather
than copied per endpoint group:

> *"One copy, because two would drift and the drift would be a tenancy hole."*

Identity has **three** endpoint files that need it, so it gets the
`EventIngestionFabResolution` treatment rather than CameraCatalog's
private-static-per-file treatment. One file, three callers.

```csharp
// src/Identity/Api/IdentityFabResolution.cs  (new, internal static)
public static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
    ClaimsPrincipal user,
    string fabId,
    IFabAuthorizationGuard fabGuard,
    string unusableFabErrorCode,   // KIOSK_ / DEVICE_ / WEBHOOK_INVALID_INPUT
    CancellationToken cancellationToken)
```

The error-code parameter is the one deviation from the three existing wrappers,
and it is the established way to parameterise this family:
`FabResolution.ResolveForWriteAsync` already takes an `ambiguousErrorCode` for
exactly this reason. The alternative — one wrapper per endpoint file, or a single
hard-coded `IDENTITY_` code — either reintroduces the triplication or silently
renames three existing error codes.

Body is `EventIngestionFabResolution.ResolveReadFabsAsync` unchanged, including
the **per-entry** parse (a caller with one unusable `/fabs/` group still sees the
fabs they legitimately hold) and the `fabs.Count == 0` → 400 fallthrough.

### The endpoint, after

```csharp
private static async Task<IResult> List(
    [FromServices] IFabAuthorizationGuard fabGuard,
    [FromServices] ListKiosksQueryHandler handler,
    ClaimsPrincipal user,
    CancellationToken cancellationToken,
    [FromQuery] string fabId = "")          // was string? fabId = null
{
    Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
        await IdentityFabResolution.ResolveReadFabsAsync(
            user, fabId, fabGuard, "KIOSK_INVALID_INPUT", cancellationToken);
    if (fabsResolution.IsFailure)
    {
        return fabsResolution.Error;
    }

    Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
        await handler.HandleAsync(new ListKiosksQuery(fabsResolution.Value), cancellationToken);

    return result.Match<IResult>(onSuccess: Results.Ok, onFailure: error => error.ToProblem());
}
```

`string fabId = ""` rather than `string? fabId = null` mirrors
`CameraEndpoints.List:340` and keeps the non-nullable `string` that
`ResolveForReadAsync` declares — no null-forgiving operator, no `?? ""`.

**The raw `fabId` goes to the resolver unparsed.** That is what produces spec
AS-5's deliberate `400 → 403` change for a malformed-but-non-blank fab, and it is
what all six existing adopters do. Do not add a pre-parse to "preserve" the 400:
it would make Identity the only context where a malformed fab name is
distinguishable from a well-formed one the caller does not hold.

### Why this is Shape A-for-reads and not spec 180's Shape C

Spec 180 (`plan.md` §2) had to put the fab **in the lookup predicate** because a
guard alone passed honestly when the attacker named their *own* fab. That attack
has no analogue here: there is no caller-supplied target to look up. The fab set
*is* the predicate, it comes from the token rather than the request, and the
caller cannot name a fab they do not hold — `EnsureAccessAsync` refuses that on
the explicit path and `FabClaims.AssignedFabs` cannot produce it on the implicit
one. This is the read-side shape, and it is the shape the issue asks for.

## 3. Entities, value objects, invariants

Nothing new. What the change rests on, all pre-existing:

| Type | Where | Relevance |
|---|---|---|
| `RegisteredClient` (aggregate) | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs` | already carries `Fab` (`FabIdentifier`), `Kind`, `Version`, `DisabledAt`, `LastRotatedAt` |
| `FabIdentifier` | `src/Identity/Domain/RegisteredClient/FabIdentifier.cs` | Identity's own, per ADR-0044 — no shared type is introduced |
| `ClientKind` | same folder | unchanged; still the first filter in the projection |
| `RegisteredClientSummaryDto` | `src/Identity/Application/DTOs/` | **untouched** |

**Invariant made structural:** *a registered-client listing returns rows only
from fabs the caller holds.*

The mechanism is the type, not a code path: `Option<FabIdentifier> Fab` can
represent "no filter", so any handler holding one has to remember to branch.
`IReadOnlyList<FabIdentifier> Fabs`, resolved at the trust boundary and never
empty (the resolver 400s instead of returning `[]`), has **no unfiltered state to
represent**. That is SC-005, and it is the same reasoning
`ICameraRepository.GetWithinFabAsync` records for the write side — *"a caller
cannot forget the check"*.

**ADR-0141 is not violated by removing an `Option<T>` here.** ADR-0141 prefers
`Option<T>` to a nullable parameter *for an absence that is real*. After this
change there is no absence: a list request always concerns a definite, non-empty
set of fabs. Replacing `Option.None`-means-everything with a resolved set removes
a state, which is the point.

**Invariant deliberately preserved:** the `ClientKind` filter runs first and is
unchanged, so a device is still invisible through `/kiosks`; ordering stays
`OrderByDescending(client => client.Registration.At)`; disabled rows are still
included (the management UI shows enrolment history and filters client-side).

## 4. Domain and integration events

**None.** This is a read path. No domain event is raised, no integration event is
published, no `Shared.Contracts` message changes, no `V<N>` bump. The outbox is
not involved.

## 5. Boundary rules

- No cross-context project reference added; NetArchTest's existing rules are
  unaffected and are run rather than reasoned about.
- `src/ServiceDefaults/` is **not modified**. `FabResolution` is consumed exactly
  as the other six sites consume it, including the global 403 mapping in
  `FabAuthorizationExceptionHandler` (registered via `AddServiceDefaults`, which
  `src/Identity/Api/Program.cs:7` already calls).
- `FabIdentifier` stays per-context (ADR-0044). `ResolveForReadAsync` returns
  `IReadOnlyList<string>` precisely so `ServiceDefaults` need reference no
  context's VO; `IdentityFabResolution` is where the parse happens, in Api.
- Domain stays pure. The guard is called in Api only — never in Application, never
  in Domain. The query record carries an already-authorised fab set.
- The read seam stays `IRegisteredClientQuerySource`; EF translation continues to
  happen in Infrastructure.

## 6. Ordering and statuses

The endpoint's refusal order, and it is load-bearing:

1. **Scope policy** (already, on the route group) → 401 / 403.
2. **`ResolveReadFabsAsync`**, before anything else is evaluated:
   - explicit `fabId` the caller lacks (or a malformed one) → **403
     `RESOURCE_FAB_NOT_AUTHORIZED`**;
   - omitted and the caller holds no fab → **403**, detail *"Caller is not a
     member of any fab."*;
   - resolved, but no entry parses as a `FabIdentifier` → **400
     `<PREFIX>_INVALID_INPUT`**.
3. **Handler + projection** → always **200**, possibly an empty array.

Nothing about any client is read until the fab set is known, so a refusal can
never be shaped by what exists in another fab.

**`Architecture.Tests/ConcurrencyConflictDeclarationTests` needs no edit.** Its
route register keys on **path**, and no path changes; no new
`HttpStatusCode.Conflict` variant is introduced. Phase 4 runs the suite to
confirm rather than assume — if it goes red, that is a finding, not a file to
adjust.

## 7. The projection change, in full

```csharp
public static async Task<IReadOnlyList<RegisteredClientSummaryDto>> ListAsync(
    IRegisteredClientQuerySource source,
    ClientKind kind,
    IReadOnlyList<FabIdentifier> fabs,
    CancellationToken cancellationToken)
{
    Ensure.That(source).IsNotNull();
    Ensure.That(fabs).IsNotNull();

    return await source.RegisteredClients
        .Where(client => client.Kind == kind && fabs.Contains(client.Fab))
        .OrderByDescending(client => client.Registration.At)
        .Select(...)                       // unchanged
        .ToListAsync(cancellationToken);
}
```

`fabs.Contains(client.Fab)` is the established predicate —
`ListCamerasQueryHandler:46` and `ListStreamsQueryHandler:37` — and Identity's
`Fab` column carries the identical `HasConversion` shape as CameraCatalog's, so
it translates the same way. Verified on the real stack at phase 4, not asserted
here. The in-memory `TestAsyncEnumerable` fake needs no change.

The **doc comment must change too**: it currently says *"an optional
`FabIdentifier`"*, which after this is false. A comment left asserting an
optionality that no longer exists is the clerical defect §IV's leg table has been
corrected for twice.

## 8. Test plan (phase 4a writes these; colour declared in `tasks.md`)

### Integration — the counterfactual that carries the whole claim

New: `tests/Integration.Tests/Identity/CrossFabListIntegrationTests.cs`, modelled
on `tests/Integration.Tests/Identity/CrossFabDisableIntegrationTests.cs` (spec
180) and reusing its fixture helpers verbatim where they fit.

Each test creates its own throwaway client over HTTP. Enrolment mints a **real**
Keycloak client, so rows are never wiped — the reasoning
`IdempotentRegistrationIntegrationTests` records at its head applies unchanged.
Prefix ids with `s183-` so a failed run is identifiable in the realm.

| # | Test | Before fix | After fix |
|---|---|---|---|
| I1 | A munich operator listing kiosks without a fabId does not see a dresden kiosk | **fails — the dresden `clientId` is in the body** | passes; every row's `fab` is `munich` |
| I2 | Same, for `/devices` | **fails** | passes |
| I3 | Same, for `/webhook-integrations` | **fails** | passes |
| I4 | A multi-fab operator listing without a fabId sees munich **and** dresden, and no berlin | passes (vacuously — they see everything) | passes (for the right reason) |
| I5 | `?fabId=dresden` from a munich-only operator is still 403 `RESOURCE_FAB_NOT_AUTHORIZED` | passes | passes |
| I6 | `?fabId=munich` from a munich operator still returns only munich rows | passes | passes |

**I1-I3 are the red, and the assertion is on the body, not the status.** The
status is `200` before and after; a test asserting a status code here could not
fail. Assert that the dresden `clientId` **is not present** and that the set of
distinct `fab` values is exactly `{munich}` — an assertion whose text does not
change when the subject does, and which is red today for the right reason.

**I4 is the over-narrowing guard and must be written even though it passes
today.** Without it, a fix that resolved a single fab would ship green.

Tokens, all via `aspire.CreateAuthenticatedClientAsync("identity", …)`:
`admin@munich.test`/`Admin1234`, `op-dresden@dresden.test`/`Operator1234`,
`op-multi@smart-sentinel-eye.test`/`Operator1234`,
`op-berlin@berlin.test`/`Operator1234`. All go through `smart-sentinel-eye-web`
with `sse.management`, which the legacy bundle accepts for every scope these
routes require — **no fixture change and no realm change is needed**, and the
listing principal is an ordinary seeded operator rather than one invented for
the test.

### Unit — Application

`tests/Identity.Application.Tests/Queries/ListKiosksQueryHandlerTests.cs` and
`ListDevicesQueryHandlerTests.cs` exist (**4 and 6 cases**, 10 query
constructions between them — counted) and **must** be updated: every construction
passes `Option<FabIdentifier>` and will not compile otherwise.
Beyond the mechanical update, add to each:

- a client in a fab **not** in the list is excluded;
- a list of **two** fabs returns rows from both (the multi-fab case, at handler
  level);
- the `ClientKind` filter still excludes other kinds when the fab matches.

No `ListWebhookClientsQueryHandlerTests.cs` exists today. **Adding one is not
required**: the three handlers are one-line delegations to the same projection,
that projection is exercised by the two existing suites, and I3 covers the
webhook endpoint end to end. Adding a third near-identical unit file is
speculative duplication (ADR-0036). If coverage gates (ADR-0065, Application ≥
80%) come up short, add it then — phase 4b measures rather than guesses.

### Not written

- **No e2e spec.** None of the three routes has a UI (spec §7 A2 — zero frontend
  callers).
- **No new architecture test.** A rule like *"every list endpoint resolves a fab
  set"* would be worth having and is genuinely tempting here, since this is the
  third member of one defect class. It is **out of scope**: it is a new
  enforcement decision, which the lane may not make (ADR-0144), and it would need
  to be written against nine contexts rather than one. Worth filing as a
  follow-up; not worth smuggling in.
- **No new zero-fab integration test.** `FabResolutionTests` already covers the
  `ForNoFabMembership` throw at the unit level, and no seeded principal that can
  reach these routes lacks a fab group (spec §7 A3).

## 9. Risks

| Risk | Mitigation |
|---|---|
| The fix narrows to **one** fab and silently breaks multi-fab operators | I4 exists for exactly this, uses the seeded `op-multi`, and is named in `tasks.md` as non-optional. `ResolveForReadAsync` returns a list by design. |
| A test asserts a status code and therefore cannot fail | Stated in `tasks.md`'s colour declaration: the red must be a **dresden `clientId` in a munich body**, not a status mismatch. An assertion that cannot fail has been the defect five times in one week. |
| `fabs.Contains(...)` does not translate and throws at runtime | Same predicate, same conversion shape, as CameraCatalog in production. Phase 4 runs it against real Postgres via the Aspire fixture — the failure would be loud and immediate, not silent. |
| The `400 → 403` change for a malformed `fabId` reads as a regression at review | Declared in spec AS-5 with its reasoning, and listed as a `security-reviewer` question. |
| The stale `Option`-mentioning doc comments survive | Named tasks (T00x), and SC-005's wording forces the reader to check. |
| Someone "fixes" the webhook list by giving it a fourth error-code prefix | The three existing prefixes are passed in, unchanged. The wrapper takes the code; it does not invent one. |
| A caller outside the repo breaks on the narrowed result | Zero callers measured twice (spec §7 A2). Both in-repo callers already send `?fabId=`. |
