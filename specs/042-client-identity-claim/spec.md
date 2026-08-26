# Feature Specification: One client scope supplies `sub`, and the realm stops listing scopes that do not exist

**Feature Branch**: `042-client-identity-claim`

**Created**: 2026-08-26 *(rewritten twice the same day — see Assumptions)*

**Status**: Draft

**Issue**: 1885 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: Every client in `src/AppHost/Realms/smart-sentinel-eye-realm.json`
lists four client scopes that do not exist in the realm. Keycloak discards them
on import. One client depends on what was discarded and mints access tokens with
no `sub` claim.

---

## Why this exists

The realm file supplies its own `clientScopes` array. Keycloak treats that as
**replacing** the built-in set rather than adding to it, so `basic`, `profile`,
`email` and `roles` do not exist in this realm. All eight clients list all four
anyway. On every import Keycloak logs, once per name per client:

```text
WARN [org.keycloak.models.utils.RepresentationToModel] Referenced client scope 'basic' doesn't exist. Ignoring
```

**Thirty-two warnings per boot, and the file goes on listing them.** Anyone
reading it — person or agent — sees four scopes applied that are not.

That silence has hidden two defects in two weeks.

**The first** was spec 041: `kiosk-web` could not list layouts, because it lacked
`sse-groups` and so had no fab claim. Diagnosing it also turned up a second
missing piece — no `sub` — which would have left every tile dark, and which was
patched with a client-level `oidc-sub-mapper` on that one client.

**The second is still in place.** `management-web` — the spec-008-v2 replacement
for `smart-sentinel-eye-web`, described in the realm as replacing it, still
unused — mints an access token with **no `sub`**.
`ClaimsPrincipalExtensions.ToOperatorIdentifier` throws
`UnattributableOperatorException` on such a token, mapped to a 401, rather than
fabricate an operator and corrupt the audit trail. **Seventeen endpoints** call
it, across LayoutComposition, OverlayDesigner, Automation, SystemVariables and
Identity. Pointing management-web at that client would 401 every one of them.

### The other six clients are fine, for a reason nothing records

| Client | Flow | `sub` | Comes from |
|---|---|---|---|
| `smart-sentinel-eye-web` | authorization code | yes | `sse.management`'s `sub-claim` mapper |
| `kiosk-web` | authorization code | yes | its own client mapper (spec 041) |
| **`management-web`** | authorization code | **no** | — |
| `identity-admin` | client credentials | yes | the grant |
| `migration-runner` | client credentials | yes | the grant |
| `stream-distribution-attribution` | client credentials | yes | the grant |
| `scenario-simulator` | client credentials | yes | the grant |
| `event-ingestion` | client credentials | yes | the grant |

For `client_credentials`, Keycloak sets `sub` from the client's service-account
user, so a mapper is irrelevant. Only user-backed flows need one. **Nothing in
the repository says this**, which is why the file appears to say otherwise.

So one fact — who holds this token — arrives three unrelated ways, two of them
accidental. `smart-sentinel-eye-web` is identifiable *only because* it holds
`sse.management`; narrow that permission and its writes silently stop being
attributable. Narrowing exactly that kind of permission is what spec 041 did to
the WHEP gate.

### Nothing checks any of it

No test asserts that a client can be attributed, or that the scopes a client
names exist. The failure is invisible three times over: the import warning goes
unread, sign-in succeeds (the **ID** token carries `sub` regardless — only the
access token lacks it), and the fault appears at the first write.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The realm file describes the realm (Priority: P1)

Every scope a client names exists. Keycloak discards nothing on import.

**Why this priority**: it is the mechanism. Fixing `management-web` while leaving
thirty-two fictional entries repairs one instance and preserves what produced it.

**Independent Test**: import the realm; count the warnings.

**Acceptance Scenarios**:

1. **Given** the realm is imported,
   **When** the Keycloak log is read,
   **Then** **zero** lines report a referenced scope that does not exist — down
   from thirty-two.
2. **Given** the realm file,
   **When** any client's `defaultClientScopes` is read,
   **Then** every entry names a scope defined in the realm's `clientScopes`.

---

### User Story 2 — Every client mints a token carrying `sub` (Priority: P1)

Each client issues access tokens with a `sub` claim, and does so because a scope
supplies it rather than as a side effect of something else.

**Why this priority**: it is the live defect, and the one waiting to be adopted.

**Independent Test**: mint a token per client; each carries `sub`.

**Acceptance Scenarios**:

1. **Given** each of the eight clients,
   **When** each mints an **access** token,
   **Then** **each** carries `sub` — checked per client, not sampled.
2. **Given** `management-web`,
   **When** an operator makes a write through it,
   **Then** `ToOperatorIdentifier` resolves and the write succeeds rather than
   401s. *(Pointing management-web at it is not in scope; being able to is.)*
3. **Given** `smart-sentinel-eye-web`,
   **When** `sse.management` is removed from it,
   **Then** it still mints `sub`.

---

### User Story 3 — Neither failure can recur silently (Priority: P1)

A client added without the identity scope, or naming a scope that does not exist,
fails a test.

**Why this priority**: equal. Both failures are silent today, and both were found
only while chasing an unrelated symptom.

**Independent Test**: break each; watch it go red.

**Acceptance Scenarios**:

1. **Given** the realm as it should be,
   **When** the checks run,
   **Then** they pass.
2. **Given** a client without the identity scope,
   **When** the checks run,
   **Then** they **fail** — demonstrated by causing it.
3. **Given** a client naming a scope the realm does not define,
   **When** the checks run,
   **Then** they **fail**, rather than the entry being discarded at import.

---

### User Story 4 — One scope supplies `sub` (Priority: P2)

A single client scope carries the `oidc-sub-mapper`. No permission scope and no
client carries one.

**Why this priority**: P2 — the system works either way today. But three
mechanisms currently supply one claim, two of them accidental, and a permission
scope that also decides identifiability is the coupling that produced this.

**Independent Test**: exactly one scope carries the mapper.

**Acceptance Scenarios**:

1. **Given** the realm file,
   **When** it is read,
   **Then** one client scope carries an `oidc-sub-mapper`, no `sse.*` permission
   scope carries one, and no client carries its own `protocolMappers`.

---

### Edge Cases

- **Service-account clients.** They get `sub` from the `client_credentials`
  grant, so they neither need the scope nor are harmed by it. They are given it
  anyway: otherwise every future client requires a judgement about which kind it
  is, and an error in that judgement is invisible until the first write.
- **Clients created at runtime.** `EnrollKioskCommandHandler` and
  `RegisterDeviceCommandHandler` create clients through the Keycloak Admin API
  with `KeycloakScopeBundles.Kiosk` / `.Device`. They are not in the realm file,
  so nothing here reaches them — and they do not need it, being service accounts.
  No test can cover them from the file.
- **`kiosk-web`'s client-level mapper**, added by spec 041. Folded in, or it is a
  second source of one claim.
- **`smart-sentinel-eye-web` losing `sse.management`.** Plausible: narrowing
  blanket permissions is active work. It must keep minting `sub` afterwards.
- **A typo in a scope name.** Currently discarded with a warning. Must fail.
- **A client that signs in cleanly and then 401s on every write.** Today's
  symptom for `management-web`, and why it is hard to spot: the ID token carries
  `sub` regardless.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: No client's `defaultClientScopes` may name a scope absent from the
  realm's `clientScopes`.
- **FR-002**: Importing the realm MUST produce zero `doesn't exist. Ignoring`
  warnings.
- **FR-003**: Every client in the realm MUST mint access tokens carrying `sub`.
- **FR-004**: A write through any user-facing client MUST resolve to the acting
  operator rather than 401.
- **FR-005**: Exactly **one** client scope MUST carry an `oidc-sub-mapper`, and
  every client MUST hold it.
- **FR-006**: No `sse.*` permission scope and no client may carry an
  `oidc-sub-mapper` of its own.
- **FR-007**: A test MUST fail when a client lacks the identity scope.
- **FR-008**: A test MUST fail when a client names a scope the realm does not
  define.
- **FR-009**: No claim beyond `sub` may be added. Nothing in `src/` reads
  `preferred_username`, `email`, `realm_access` or `resource_access`.
- **FR-010**: No client's effective permission set may change. The `scope` claim
  each client mints MUST be identical before and after.
- **FR-011**: The record MUST state why `client_credentials` clients are
  unaffected — Keycloak sets `sub` from the service-account user, this is written
  down nowhere, and without it the six working clients look like luck.

### Key Entities

- **Client**: a Keycloak client. Eight in the realm file; more created at runtime
  by Identity.
- **Client scope**: a named bundle. `sse.<noun>.<verb>` grants a permission and
  appears in the `scope` claim; `sse-groups` carries a claim and sets
  `include.in.token.scope: false`. The realm follows this split without stating
  it.
- **`sub` claim**: the subject of an access token. Read by
  `ToOperatorIdentifier` (17 endpoints) and by `WhepAuthValidator`, both of which
  fail closed without it.
- **`sse.management`**: the grandfathered blanket permission. Currently also the
  only realm scope carrying identity mappers.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **Zero** `doesn't exist. Ignoring` warnings on import, down from
  thirty-two.
- **SC-002**: **All eight** clients mint an access token carrying `sub` —
  measured per client, not sampled.
- **SC-003**: A client without the identity scope **fails** a test —
  demonstrated by causing it.
- **SC-004**: A client naming an undefined scope **fails** a test —
  demonstrated by causing it.
- **SC-005**: An operator's write through a minted token succeeds and is recorded
  against that operator, observed end to end.
- **SC-006**: **One** client scope carries an `oidc-sub-mapper`; **zero**
  permission scopes and **zero** clients carry one.
- **SC-007**: Every client's `scope` claim is byte-identical to before.

---

## Assumptions

- **This spec has been rewritten twice.** The first draft claimed six of eight
  clients could not mint `sub`. That was read off the realm file — which clients
  carry an `oidc-sub-mapper` — rather than from tokens, and it was wrong: one
  cannot. `client_credentials` supplies `sub` without any mapper. The error is
  recorded rather than deleted, because asserting from a configuration file what
  only a token settles is the exact failure this feature addresses. The second
  rewrite replaced deliberately non-technical vocabulary with the actual names;
  see the checklist.
- **Only `sub` is needed.** `preferred_username` appears once in `src/`, as
  `WhepAuthValidator`'s `NameClaimType`, whose resulting `Name` nothing reads.
  Recreating `profile`, `email` or `roles` would be speculative generality
  (ADR-0036).
- **`ToOperatorIdentifier`'s fail-closed behaviour is correct and unchanged.**
  This removes the reason it fires.
- **No production deployment exists**, so changing every client coordinates with
  nothing. The realm is re-imported from this file on a developer's machine.
- **Spec 041's client-level mapper was right to be narrow.** It fixed one screen
  without touching seven other clients. This is the general version.

---

## Out of Scope

- **Pointing management-web at the `management-web` client.** A separate
  decision and an app change. This makes it possible.
- **Any change to what a client may do.** Identity is not permission.
- **`KeycloakScopeBundles`** and the runtime client-creation paths — those
  clients already carry `sub`.
- **Issue 1886** (management-web renders no video) and **issue 1891** (the
  overlay-draw metric), both filed separately.
- **Any production rollout.**
