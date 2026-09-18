# Plan 182 — A rotation that cannot cross a fab

**Spec:** `specs/182-a-rotation-that-cannot-cross-a-fab/spec.md`
**Issue:** #2280

---

## 1. Bounded contexts and layers touched

Two contexts. **No shared file between them**, so the two halves are `[P]`
parallel (ADR-0109). Nothing crosses a context boundary: the only thing both
sides read is `EventMetadata.Fab`, which lives in `Shared.Contracts` and is
**unchanged** — no new field, no `V<N>` (ADR-0073).

### Identity (US1)

| Layer | File | Change |
|---|---|---|
| Api | `src/Identity/Api/WebhookRotationEndpoints.cs` | **none** — the guard and the required `fabId` are already there (spec §8 A1) |
| Application | `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs` | `GetByClientIdAsync(clientId, …)` → `GetWithinFabAsync(fab, clientId, …)`; correct the stale comment at `:129-131` that names `GetByClientIdAsync` |
| Domain | `src/Identity/Domain/RegisteredClient/IRegisteredClientRepository.cs` | **none** — `GetWithinFabAsync` already exists (spec 180) |
| Infrastructure | `src/Identity/Infrastructure/Persistence/RegisteredClientRepository.cs` | **none** |

**One production line changes in Identity.** That is the whole of US1's `src/`
diff, and it is the point: the fix the repo already built is being applied to the
one handler that was missed.

### EventIngestion (US2)

| Layer | File | Change |
|---|---|---|
| Domain | `src/EventIngestion/Domain/WebhookIntegration/IWebhookIntegrationRepository.cs` | **add** `GetWithinFabAsync(FabIdentifier fab, WebhookIntegrationName name, CancellationToken)` |
| Infrastructure | `src/EventIngestion/Infrastructure/Persistence/WebhookIntegrationRepository.cs` | implement it — `.Where(i => i.Name == name).Where(i => i.Fab == fab)` |
| Application | `src/EventIngestion/Application/EventHandlers/WebhookIntegrationRotatedV1Handler.cs` | bind `metadata`; parse `FabIdentifier` from `metadata.Fab`; refuse + log when absent or unparsable; resolve via the scoped lookup |
| Application | `src/EventIngestion/Application/Log.cs` | **add** one `[LoggerMessage]` — `RotationFabMissing` / `RotationFabMismatch` (see §5) |

**`GetByNameAsync` stays and keeps its only other caller.** The ingest path
(`POST /events/webhook/{name}`) resolves by name because the name *is* the route
and no caller fab exists there. Removing it would be a different change.

## 2. Entities, value objects and invariants

**No new aggregate, no new value object, no migration.** Everything the fix
needs is already modelled:

| Type | Where | Role here |
|---|---|---|
| `RegisteredClient` (aggregate) | `Identity/Domain/RegisteredClient` | already carries `Fab : FabIdentifier`; the predicate reads it |
| `ClientId` (VO) | same | `webhook-{name}`, globally unique among active rows (`ux_registered_clients_clientid_active`) |
| `FabIdentifier` (VO) | `Shared.Kernel` | the scoping key on both sides |
| `WebhookIntegration` (aggregate) | `EventIngestion/Domain/WebhookIntegration` | already carries `Fab : FabIdentifier` and `ValidationMode` |
| `WebhookIntegrationName` (VO) | same | globally unique (`ux_webhook_integrations_name`), deliberately |
| `EventMetadata` | `Shared.Contracts` | `Fab : string?` — the wire form, primitive by ADR-0040 |

**Invariant being added, stated once and in one place each:**

> **INV-1 (Identity).** A rotation may only resolve a `RegisteredClient` whose
> `Fab` equals the fab the caller was authorised for. Enforced **in the query
> predicate**, not by a comparison afterwards, so an unscoped materialisation
> cannot happen by omission.

> **INV-2 (EventIngestion).** A `WebhookIntegrationRotatedV1` may only mutate a
> `WebhookIntegration` whose `Fab` equals the event's `Metadata.Fab`. Same
> enforcement: in the predicate. An event with no fab satisfies no predicate and
> is refused.

Neither invariant lives on an aggregate, because neither is a statement about a
single aggregate's internal consistency — both are statements about *which*
aggregate a request is permitted to reach. That belongs at the repository
boundary, which is where spec 180 and `ICameraRepository` already put it.

**No index is needed.** Identity: `ux_registered_clients_clientid_active` already
resolves `client_id` to at most one active row; adding `fab` to the `WHERE`
filters that single row. EventIngestion: `ux_webhook_integrations_name` does the
same for `name`. Both scoped lookups are still single-row index seeks, so no
migration and no measurable cost.

## 3. Control flow after the change

### US1 — `POST /webhook-integrations/{name}/rotate`

```
scope policy (sse.webhooks.write)          → 401 / 403
Idempotency-Key parse                      → 400
FabIdentifier.From(body.FabId)             → 400  WEBHOOK_INVALID_INPUT
IFabAuthorizationGuard.EnsureAccessAsync   → 403  RESOURCE_FAB_NOT_AUTHORIZED   ← AS-3
If-Match / If-None-Match parse             → 400 / 428
idempotency replay short-circuit           → 200 (original answer, ADR-0142)
ClientId.From("webhook-" + name)           → 400  WEBHOOK_INVALID_INPUT
clients.GetWithinFabAsync(fab, clientId)   ← THE CHANGE
  ├ Some + If-Match  → version check → rotate → 200 / 409 WEBHOOK_CLIENT_STALE
  ├ Some + If-None-Match:* → 412 WEBHOOK_CLIENT_ALREADY_EXISTS
  ├ None + If-Match  → 412 WEBHOOK_CLIENT_NOT_FOUND                             ← AS-4, AS-9
  └ None + If-None-Match:* → create branch                                      ← AS-2, and AS-5's first move
```

**The ordering is unchanged and remains correct.** The guard still runs before
the precondition is read, so a caller who may not touch the fab gets 403 rather
than 428 and an invitation to retry — the comment at `WebhookRotationEndpoints.cs:126-131`
already says this and stays true.

**AS-4 and AS-9 become the same answer, which is the security property.**
`WebhookClientNotFound` carries only the `clientId` the caller typed and the
version they sent — nothing read from the victim's row. A caller cannot
distinguish "no such integration anywhere" from "exists in a fab you do not
hold". **Do not add a `WEBHOOK_CLIENT_WRONG_FAB` code**; it would be the
enumeration oracle the 412 exists to avoid.

### US2 — the `WebhookIntegrationRotatedV1` subscriber

```
Ensure.That(message).IsNotNull()
var (integrationName, clientId, _, metadata) = message;      ← metadata no longer discarded
WebhookIntegrationName.From(integrationName)  → log InvalidRotationName, return  (unchanged)
metadata.Fab is null / unparsable             → log RotationFabMissing, return   ← NEW
integrations.GetWithinFabAsync(fab, name)     ← THE CHANGE
  ├ None → log RotationTargetMissing, return   (unchanged path, new reachability) ← AS-5, AS-6
  └ Some → MarkAsRotated + SaveAsync           (unchanged)
```

**A refusal is a log and a return, not a throw.** `WebhookIntegrationRepository.SaveAsync`
dispatches domain events *before* the commit, so a handler that throws fails the
enclosing write. Every existing refusal on this handler (`InvalidRotationName`,
`RotationTargetMissing`) already returns quietly and Wolverine treats the message
as handled; a throw would instead retry a message that will never succeed and
eventually dead-letter it. Matching the established shape is both correct and
smaller.

**The deconstruction must bind `metadata`, not a differently-named local.**
`HandlerDeconstructionTests` reads source and fails the build when a local is
named after a *different* field of the same record (CLAUDE.md, house rules).
`metadata` matches `Metadata`; a rename to e.g. `clientId`-adjacent spellings
does not.

## 4. Messaging — domain → integration event

**Nothing changes.**

- `WebhookIntegrationRotatedV1` keeps its four members. No `V2` (ADR-0073): no
  field is added, removed or retyped.
- `RotateWebhookClientCommandHandler` keeps publishing it with
  `Metadata.Fab = fab.Value`, which is what US2 reads. This is the one existing
  producer; confirmed by grep across `src/`.
- The outbox flow is untouched: `SaveAsync` dispatches, `commit.CommitAsync`
  flushes, both already present and unmoved.
- **US1 reduces what is published**, it does not change its shape: a cross-fab
  rotate now returns 412 before reaching the publish.

## 5. Logging (ADR-0050)

One new `[LoggerMessage]` in `src/EventIngestion/Application/Log.cs`, placed
beside `RotationTargetMissing` and following its wording style:

```
Level = Warning
"Webhook integration '{Name}' rotation event carried fab '{Fab}' and was ignored."
```

`Warning`, not `Information`: `RotationTargetMissing` is benign (a replay against
a deleted integration), whereas a fab mismatch means either an attack or a
publisher defect and someone should see it. A `null` fab is logged through the
same message with `"(none)"`, so there is one signal to alert on rather than two.

**Do not log the fab the *integration* is in.** The message is emitted on a path
an attacker can trigger; naming the victim's fab in a log an operator of the
attacking fab may be able to read would re-open the enumeration leak in a
different medium. The caller-supplied fab is already known to the caller.

## 6. Boundary rules — checked, not assumed

- **No cross-context project reference.** Identity does not reference
  EventIngestion and does not gain one. This is precisely why US2 must exist
  (spec §2): Identity cannot ask who owns a webhook integration name.
- **Only `Shared.Contracts` crosses.** `EventMetadata` / `WebhookIntegrationRotatedV1`
  — already the channel, unchanged.
- **Domain stays pure.** The new repository method is an interface declaration in
  `EventIngestion/Domain`; the EF implementation is in `Infrastructure`. No I/O
  and no framework reference enters Domain.
- **NetArchTest.** `Architecture.Tests` must stay green; the ones with a stake
  here are `ConcurrencyConflictDeclarationTests` (registers the rotate route by
  path — the route and its 409/412 set are unchanged, so it should not move),
  `HandlerDeconstructionTests` (the new `metadata` local), and
  `PrimitiveBoundaryTests` (no primitive reaches a domain model; `metadata.Fab`
  is converted to `FabIdentifier` inside the Application layer, never stored).

## 7. Risk register

| Risk | Handling |
|---|---|
| A legitimate operator is broken by the scoped lookup | Only possible if a `RegisteredClient` row's `Fab` disagrees with the fab an operator holds for that integration. Rows are written with `fab` from the same authorised request, so they agree by construction. AS-1/AS-2 assert it. |
| The two existing `WebhookIntegrationRotatedV1HandlerTests` construct `Fab: null` | Their **setup data** is corrected to the integration's fab; assertions untouched. Declared in spec §5 AS-6 and in `tasks.md` so it is not read as an engineer bending a test. |
| Integration tests need two fabs and a real Keycloak | `AspireFixture` + `RealmProbe` already do this in `CrossFabDisableIntegrationTests` (spec 180). Reuse, do not invent. |
| The AS-5 test is slow / flaky (bus round trip) | Assert the *effect* by polling the integration's validation mode with the fixture's existing wait helper rather than sleeping; if a bus-timing wait proves flaky, the unit-level AS-6 pair is the load-bearing evidence and the integration assertion may be narrowed to the Identity-side outcome — **only with that trade recorded in the PR**, never by deleting an assertion. |
| Residual name-squat (spec §3.1) | Out of scope by decision, recorded in the PR body, follow-up recommended. Not silently dropped. |

## 8. What this plan explicitly does not do

- Does not touch `WebhookRotationEndpoints.List` or `ListWebhookClientsQueryHandler` (#2281).
- Does not touch `RequireScopeExtensions.LegacyManagementBundle` (console-bundle issue).
- Does not touch `EnrollKioskCommandHandler` or `RegisterDeviceCommandHandler`.
- Does not add an `ApiError` variant, a migration, an index, a contract version,
  or a new repository method on the Identity side.
- Does not make `WebhookIntegrationName` per-fab.
