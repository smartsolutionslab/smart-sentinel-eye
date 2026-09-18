# Verification 182 — A rotation that cannot cross a fab (#2280)

**Latency: N/A.** Identity/EventIngestion administrative write paths, not on
the event→overlay path constitution §IV budgets.

## Phase 4a — red (ADR-0139), quoted from commit `d700a5ab`, independently reproduced

Unit level (`RotateWebhookClientCommandHandlerTests.cs`):

```
Shouldly.ShouldAssertException : result.IsSuccess
    should be False but was True

Additional Info:
    a munich-only operator naming its own fab must not be able to rotate
    Dresden's webhook client — that is credential disclosure plus
    destruction; succeeded with clientId 'webhook-dresden-line-3' and
    clientSecret 'secret-webhook-dresden-line-3-rotated'
```

Integration level, against a freshly-booted `AspireFixture` (`CrossFabWebhookRotationIntegrationTests.cs`) — reproduced independently:

```
Shouldly.ShouldAssertException : attack.StatusCode
    should be HttpStatusCode.PreconditionFailed but was HttpStatusCode.OK

Additional Info:
    a munich-only operator naming its own fab must find no client to
    rotate; got 200 {"registeredClientIdentifier":"...","version":1,
    "clientId":"webhook-t182-...","integrationName":"t182-...",
    "fab":"munich","clientSecret":"..."}
```

```
Shouldly.ShouldAssertException : currentSecret
    should be "<pre-attack secret>" but was "<different secret>"

Additional Info:
    a refused cross-fab rotation must never reach Keycloak; a changed
    secret here means the attack actually rolled Dresden's live
    credential — the disclosure-plus-destruction this spec exists to close
```

(read via `RealmProbe`/Keycloak's own Admin API, not our `registered_clients`
row — spec 121 / #2165 / #2207 is the proof those can disagree)

US2/EventIngestion (`WebhookIntegrationRotatedV1HandlerTests.cs` + `CrossFabWebhookRotationEffectIntegrationTests.cs`):

```
Shouldly.ShouldAssertException : integration.ValidationMode
    should be BearerValidationMode.StaticHash but was BearerValidationMode.Jwt

Additional Info:
    a rotation announcement carrying fab 'munich' must never flip an
    integration registered in 'dresden'; got Jwt — that is the cross-fab
    takeover this spec exists to close
```

```
Shouldly.ShouldAssertException : mode
    should be BearerValidationMode.StaticHash but was BearerValidationMode.Jwt

Additional Info:
    a munich-scoped first rotation naming Dresden's integration must never
    flip it to JWT; got mode 'Jwt' with KeycloakClientId
    'webhook-dresden-first-...' — that is the cross-fab takeover this spec
    exists to close
```

AS-3 (dresden-named cross-fab attempt) and AS-1 (dresden-on-dresden happy
path) both passed throughout — confirming the fab *guard* was already
correct and only the *lookup* was unscoped, exactly as the architect's
investigation concluded.

## Phase 5 — end-to-end verification

`spec.md` §6 describes a manual `curl`-based procedure: mint munich and
dresden tokens from Aspire's proxied Keycloak, register and rotate a
throwaway dresden integration, attack it as munich, confirm the response
shape and read Keycloak's own secret back before/after.

**The automated integration test suite performs exactly this procedure**,
not a proxy for it: `CrossFabWebhookRotationIntegrationTests` and
`CrossFabWebhookRotationEffectIntegrationTests` mint real tokens against
Aspire's proxied Keycloak endpoint (`aspire.CreateAuthenticatedClientAsync`),
make real HTTP calls against the real endpoints, and read the victim's
actual state back through Keycloak's own Admin API (`RealmProbe`) rather
than the application's own row — the same standard spec 180's verification
used. `AspireFixture` boots a fresh stack per run (ADR-0103), so this
evidence is not subject to the "live stack can outlive the code it runs"
trap a persistent `dotnet run` stack would be.

Independently re-verified by the orchestrator (not merely trusted from
agent reports) at the final commit, `c4457620`:

- `dotnet build -c Release` (both an incremental and a forced
  `-t:Rebuild`) — 0 errors, and confirmed no S138 warning on
  `WebhookIntegrationRotatedV1Handler.cs` after the phase-6 method-length
  fix (only pre-existing advisories elsewhere).
- `Identity.Application.Tests` — 65/65.
- `EventIngestion.Application.Tests` — 90/90.
- `Architecture.Tests` — 401/401 (`HandlerDeconstructionTests`,
  `EventMetadataFabDeclarationTests`, `PrimitiveBoundaryTests`,
  `ConcurrencyConflictDeclarationTests` included).
- `Integration.Tests`, filtered to `CrossFabWebhookRotation*` — **6/6**
  green, including the phase-6 positive control (a same-fab first rotation
  genuinely reaches `Jwt` within the poll window, proving the new
  fab-scoped predicate matches a real row, not only that it correctly
  returns `None`).

US2's state read-back (step 6 of spec.md §6) is the `CrossFabWebhookRotationEffectIntegrationTests`
assertions directly: after the attack, `validation_mode` stays `StaticHash`,
`keycloak_client_id` stays null, and the original static bearer is still
accepted at `POST /events/webhook/{name}` — asserted end-to-end, not
inferred.

## Phase 6 — review, findings closed

Both `security-reviewer` and `backend-reviewer` ran (security review
explicitly required here, not optional — a cross-fab credential-theft fix).
**No blockers from either.** Findings, and how each was resolved:

1. **(both reviewers, independently) The new Warning-level log could never
   fire on the actual attack.** `RotationFabMismatch` was only reachable
   from the catch-block for a null/unparsable fab — a case spec.md §8 A5
   says never happens in production. The real attack (a well-formed fab
   naming an integration in a different fab) fell through to the
   pre-existing `RotationTargetMissing` at Information level, worded as a
   benign "not present" — indistinguishable from a replay against a
   deleted integration. **Fixed**: the refusal path now does a plain
   `GetByNameAsync` read purely to discriminate (a lookup the handler
   already had access to, never reaching the caller, never changing the
   refusal) — `RotationFabMismatch` (Warning, caller's own claimed fab
   only) when the integration exists in a different fab; `RotationTargetMissing`
   (Information, unchanged) when it genuinely doesn't exist anywhere. The
   victim's real fab is never logged either way.

2. **(backend-reviewer) `metadata` was dereferenced unguarded — the one
   input shape the design's own stated rule says must never throw.**
   `EventMetadata` is non-nullable in the contract, but a deserialized
   message missing `Metadata` yields a genuine `null` at runtime regardless
   of that annotation, and this handler had just started reading the field
   it previously discarded. **Fixed**: adopted the guard shape already
   established in `SystemVariableValueRequestedV1Handler.Handle` —
   `string.IsNullOrWhiteSpace(metadata?.Fab)` — so a null `Metadata`
   refuses quietly (log-and-return) rather than crashing and dead-lettering
   a message that can never succeed.

3. **(backend-reviewer) A new S138 (method length) crossing on a forced
   clean rebuild**, invisible on an incremental build. **Fixed**: extracted
   `ParseFab` and `LogRefusalAsync` as private helpers; confirmed via a
   forced `dotnet build -c Release -t:Rebuild` that the warning is gone.

4. **(backend-reviewer) No positive control for the new fab-scoped
   predicate** — the AS-5 integration test only ever exercised the `None`
   branch, so a regression that broke the equality check (and made every
   legitimate same-fab rotation silently stop flipping to JWT) would never
   have gone red. **Fixed**: added a same-fab first-rotation case to the
   same test class, asserting it *does* reach `Jwt` within the poll window.

Nits closed alongside (comment attached to the wrong statement, a dropped
exception in one catch clause, a garbled doc-comment sentence) — all
cosmetic, bundled into the same fix commits.

## Accepted in writing, not fixed — spec §3.1's residual

Both reviewers independently confirmed spec.md §3.1's residual is real and
correctly characterised as **denial-of-rotation, not takeover**:

An attacker's *first* cross-fab rotation attempt still creates a Keycloak
client named `webhook-<victim-name>` in the *attacker's own* fab before the
EventIngestion-side refusal fires — because Identity's `GetWithinFabAsync`
correctly returns `None` on a first rotation (no row exists in any fab yet),
so the *create* branch legitimately runs. This squats the globally-unique
integration name and permanently blocks the victim's own future first
rotation of that name (Keycloak's client-existence probe throws for the
realm-global clientId; `If-Match` finds no row in the victim's fab).

**Not takeover**, confirmed independently by the security-reviewer tracing
every gate: `IsIntegrationsOwnFab` requires the caller's own fab on the
ingest path before a token is even examined; the victim's integration stays
`StaticHash` (US2 prevents the flip), so a JWT token from the squatted
client can never match; and even a flipped integration would require
`azp == KeycloakClientId` **and** a `/fabs/<victim>` group claim, which the
squatted client (in the attacker's fab) cannot carry. The squatted
credential reads and forges nothing.

**Backend-reviewer's SF-4 widens the framing correctly**: this reachability
does not require a pre-existing unrotated victim integration at all — any
holder of `sse.webhooks.write` (every seeded operator, per the legacy
management bundle) can pre-squat arbitrary future integration names in
bulk, with no rate limit on the route. The follow-up issue should describe
"any operator can pre-squat the global webhook client-name space," not only
"an attacker's first cross-fab attempt litters one client."

Closing this needs Identity to learn webhook-integration ownership across a
context boundary it does not currently reference — an ADR-level decision
the autonomous lane may not make (ADR-0144). **Recommend filing a follow-up
issue plus an ADR discussion**; not filed by the lane.

Two smaller residuals recorded, not fixed, correctly out of scope for this PR:

- **The `If-None-Match: *` branch remains a narrower cross-fab existence
  oracle** (security-reviewer SF-3): a munich operator can still learn
  whether a name has *ever* been rotated anywhere (502 `KEYCLOAK_UNAVAILABLE`
  vs 200) — narrower than the pre-fix leak (which included the victim's
  version number), but not closed. Collapsing it into the same 412 shape
  is a scope question (it would change the answer for a legitimate caller
  whose Keycloak is genuinely inconsistent) — belongs in the same follow-up
  as the name-squat, not decided here.
- **A legitimate multi-fab operator's typo has a new failure mode**
  (security-reviewer SF-2): an operator holding both `munich` and `dresden`
  who mistypes `fabId` on a first rotation gets a 200 with a live secret
  for a client that silently can't flip validation mode, and has now
  permanently blocked their own correct-fab first rotation. Strictly safer
  than the pre-fix outcome (which would have hijacked the victim
  integration instead), but worth recording as a UX/support-burden note in
  the PR rather than leaving it to be rediscovered.

Untouched, as declared: #2281 (the list-endpoint leak), the console-bundle
authorization issue, `EnrollKioskCommandHandler`/`RegisterDeviceCommandHandler`
(same method name, different defect class — legitimately unscoped there).
