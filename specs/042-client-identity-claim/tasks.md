# Tasks: The configuration stops discarding what it says

**Feature**: `042-client-identity-claim` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1885 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**15 tasks across five phases.** One scope added, three mappers removed,
thirty-two list entries deleted, two checks, and a verification that has to be
done one identity at a time.

**It changes every identity in the realm at once.** That is why Phase 0 minted a
credential per identity against a candidate realm *before* this list was written,
and why Phase 5 does it again against the real one.

**The first draft of this feature's own spec was wrong**, and wrong in exactly
the way the feature is about: it counted six broken identities by reading the
file, when one is broken and only a token could say so. That correction is in
[spec.md](./spec.md)'s Assumptions and in
[research.md](./research.md) R1. It is the reason every assertion below is
per-identity rather than sampled.

**Phases 1, 2 and 3 all edit the same file** — `smart-sentinel-eye-realm.json` —
so none of them are parallel with each other. Unusual, and worth saying, because
the phase boundaries here are about *meaning* rather than about what can run at
once.

---

## Do not

- **Do not restore `profile`, `email` or `roles` in any form.** Three of the four
  discarded names carry claims nothing in the product reads. Recreating them
  would be building for needs that do not exist (ADR-0036).
- **Do not add the new scope to `KeycloakScopeBundles`.** Clients created at
  runtime already carry the subject — measured (research R3) — so it would be
  inert for them, and it would put a non-permission into a bundle that
  `KioskScopeParityTests` compares against a permission list.
- **Do not soften the refusal.** Refusing to record an unattributable change is
  deliberate and correct. This feature removes the reason it fires; it does not
  touch the refusal.
- **Do not change what any identity is permitted to do** (FR-010, SC-007). This
  is about who a credential says you are, not what it lets you do.
- **Do not restart the Keycloak container to pick up a realm edit — the volume
  has to go.** It is `Persistent` with a data volume, the realm imports only into
  an empty database, and the stack boots healthy either way serving the old
  realm. For everything except T014, skip the problem: a throwaway
  `quay.io/keycloak/keycloak:26.5` container is faster and has no volume.
- **Do not let a client `description` exceed 255 characters.** The import dies and
  the whole Aspire fixture hangs on it.
- **Do not create `data-model.md`.** Nothing persists.
- **Do not write `#1885` in any committed document.** A bare mention auto-closes
  the issue on merge.

---

## Phase 1: The shared definition

**Goal**: One place decides whether an identity can name its holder.

- [ ] T001 [US2] Add an `sse-identity` client scope to `src/AppHost/Realms/smart-sentinel-eye-realm.json`, carrying exactly one protocol mapper — `name: "sub-claim"`, `protocolMapper: "oidc-sub-mapper"`, `config: { "introspection.token.claim": "true", "access.token.claim": "true" }` — with `attributes: { "include.in.token.scope": "false", "display.on.consent.screen": "false" }`. **Mirror `sse-groups`' shape exactly**, and put it next to it: the hyphen is the convention this realm already follows without stating it — `sse-<noun>` carries a claim and grants nothing, `sse.<noun>.<verb>` grants. `include.in.token.scope: false` matters: a scope that grants nothing must not appear among what a caller may do. Describe it in one line as carrying no permission.
- [ ] T002 [US2] Add `"sse-identity"` to the `defaultClientScopes` of **all eight** clients in the same file. Per client, not "the ones that need it" — the two that work today do so by accident, and the judgement of which need it is what has to stop being made.

**Checkpoint**: one definition exists and every identity in the file holds it.

---

## Phase 2: Take the responsibility off the accidents

**Goal**: No permission decides whether its holder can be named, and no client
keeps a private copy.

- [ ] T003 [US4] Remove **both** `sub-claim` and `preferred-username-claim` from the `sse.management` client scope in `src/AppHost/Realms/smart-sentinel-eye-realm.json` — the whole `protocolMappers` array. It is a **permission**, and a permission that also decides whether you can be identified is the conflation this feature exists to remove. It is load-bearing today: `smart-sentinel-eye-web` names its holder *only* because it holds administrative authority, so narrowing that permission would silently make its actions unattributable — and narrowing exactly that kind of permission is what the previous feature did to the kiosk.
- [ ] T004 [US4] Remove the client-level `protocolMappers` block from the `kiosk-web` client in the same file — the narrow fix spec 041 added. The shared scope supplies it now. SC-006: one definition, zero private copies.
- [ ] T005 [US4] **Confirm the one behavioural change deliberately.** `preferred_username` disappears from `smart-sentinel-eye-web`'s token. Mint a token against a throwaway container and record its absence, rather than letting a reviewer find it. Nothing reads it — the only mention in `src/` is `WhepAuthValidator` setting `NameClaimType = "preferred_username"`, whose resulting `Name` no code touches — and that was verified before the mapper was removed, not after.

**Checkpoint**: three mechanisms have become one.

---

## Phase 3: Stop the file claiming what it does not do

**Goal**: Reading the configuration tells you what the system does.

- [ ] T006 [US1] Delete `"basic"`, `"profile"`, `"email"` and `"roles"` from the `defaultClientScopes` of all eight clients in `src/AppHost/Realms/smart-sentinel-eye-realm.json` — thirty-two entries. They resolve to nothing today, so this changes what the file **claims**, not what the system does. Verified safe: nothing in `src/` reads a role or email claim (no `realm_access`, `resource_access`, `ClaimTypes.Role` or `ClaimTypes.Email`); authorization is entirely scope-based through `RequireScopeExtensions`.
- [ ] T007 [US1] **Measure SC-001.** Import the edited realm into a throwaway `quay.io/keycloak/keycloak:26.5` container and count `docker logs … | grep -c "doesn't exist. Ignoring"`. **Expected `0`, down from 32.** Also confirm no other `RepresentationToModel` warning and no import error appeared — a clean import is the claim, not just a smaller number.

**Checkpoint**: the file and the system agree. US1 is complete.

---

## Phase 4: Make both failures loud

**Goal**: Neither failure can recur silently — and it is clear which check
catches which.

- [ ] T008 [US3] Create `tests/Architecture.Tests/RealmIdentityTests.cs` with four assertions, following `KioskScopeParityTests`' idiom (repo-root walk to `SmartSentinelEye.slnx`, `System.Text.Json`, one parsed document held in a static field): **(a)** every client's `defaultClientScopes` contains `sse-identity` — enumerated per client, naming the offender; **(b)** every scope any client names exists in the realm's own `clientScopes`, as a set relationship, so a typo **fails** instead of being discarded at start-up with a warning nobody reads; **(c)** no scope whose name starts `sse.` carries a `protocolMappers` array — permissions do not decide identity; **(d)** no client carries a `protocolMappers` array of its own (SC-006).
- [ ] T009 [US3] Create `tests/Integration.Tests/Identity/TokenAttributionIntegrationTests.cs`: mint a token through `AspireFixture.CreateAuthenticatedClientAsync`, perform one attributed write over HTTP (an overlay create or a system-variable define — both call `ToOperatorIdentifier`), and assert it **succeeds**. A credential that cannot be attributed produces a **401**, not a wrong value, so success is the assertion. **Nothing does this today** (research R9): every existing test fabricates its operator with `OperatorIdentifier.From(Guid.CreateVersion7())` and hands it to a handler directly, which is exactly how an unattributable client sat unnoticed. Decode the token's subject and, if the response or a read-back exposes the actor, assert it matches; if none does, say so in the test rather than implying more.
- [ ] T010 [US3] **Prove T008 can fail, both ways.** Remove `sse-identity` from one client → assertion (a) red; revert. Give a client a scope name that does not exist → assertion (b) red; revert. Record both outputs. The second is the one that matters: that failure is *currently* a start-up warning nobody reads.
- [ ] T011 [US3] **Prove the two checks are not interchangeable.** Delete the mapper from the `sse-identity` scope, leaving the scope itself in place, and run both: `RealmIdentityTests` must stay **green** and `TokenAttributionIntegrationTests` must go **red**. Record it. The cheap check reads names; only a minted token shows behaviour — and a feature that claimed one covers the other would be repeating the error this whole run has been correcting.

**Checkpoint**: US3 complete, with the limits of each check demonstrated rather than asserted.

---

## Phase 5: The verification a file cannot give

**Goal**: One credential per identity, against the real thing.

- [ ] T012 Import the final realm into a throwaway container and mint a credential for **each of the eight** identities — password grant for the three user-facing ones (enable direct access grants **on a scratch copy only**, and say so), `client_credentials` for the five background workers. Record all eight **access**-token payloads verbatim. Not the ID token: it carries the subject regardless, which is why this hid for so long. **Verbatim, not summarised** — summarising is how this feature's first draft got its own central number wrong.
- [ ] T013 From the same eight payloads, assert per identity: the subject is present; `groups` is present wherever it was before; **the `scope` claim is byte-identical to before the change** (SC-007 — the blast radius is every client at once); and `sse-identity` does **not** appear in `scope`, because it grants nothing.
- [ ] T014 **The one step that needs the real stack.** Delete the Keycloak container **and its data volume**, boot with `dotnet run --project src/AppHost`, confirm `KC-SERVICES0030: Full model import requested` and zero discarded entries, then sign into the operator console and change something. Confirm it succeeds and appears in the audit trail against the operator who made it (SC-005).
- [ ] T015 Write the verification note on the PR: the `0` that replaced `32`, all eight payloads, the absence of `preferred_username`, both check failures from T010, **and the T011 result showing which check did not fire**. Name any step not performed. A feature about a file that claimed more than it delivered does not get to do the same.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003 ─▶ T004 ─▶ T005      (all one file, sequential by necessity)
                                  │
                                  ▼
                                T006 ─▶ T007
                                  │
                                  ▼
              T008 ─▶ T010 ─┐
              T009 ─────────┴─▶ T011
                                  │
                                  ▼
              T012 ─▶ T013 ─▶ T014 ─▶ T015
```

**T001 before T002**, or the clients name a scope that does not yet exist —
which is the very failure assertion (b) is being written to catch.

**T003/T004 after T002.** Removing the accidental sources before the shared one
is in place leaves a window where `smart-sentinel-eye-web` and `kiosk-web` cannot
name their holder. The file is edited in one commit, but the order still matters
for anyone reading the diff or bisecting it.

**T011 needs both T008 and T009**, because its whole content is the difference
between them.

---

## Parallel opportunities

**Almost none, and that is the honest shape of this feature.** Phases 1–3 are six
edits to one JSON file. T008 and T009 are genuinely parallel — different test
projects, no shared state — and that is the only pair.

Marking more would be pretending. The work is small and serial; the *verification*
is what has breadth, and it is eight measurements that cannot be taken until the
edits are done.

---

## Implementation strategy

**MVP is T007** — the moment the import stops discarding anything, the mechanism
that hid two defects is gone, and that is worth more than the single broken
identity it also fixes.

**Do Phases 1–3 as one edit and one commit.** They are six changes to one file
that only make sense together: the scope, its assignment, the removal of what it
replaces, and the deletion of what never worked. Splitting them produces
intermediate states where an identity is nameable twice or not at all.

**Do not believe T001–T006 from the diff.** The whole feature exists because a
file was believed rather than measured — and its own first draft repeated that.
T007 and T012 are the answer, and they are cheap: a throwaway container, no
volume, under a minute.

**Budget real time for T014.** Deleting the volume, booting the full stack and
driving a change by hand is the only step that cannot be shortcut, and it is the
only one that exercises the audit trail.

---

## Three things most likely to go wrong

1. **The realm is edited and nothing is verified, because the stack looks fine.**
   The container is persistent with a data volume: the edit has no effect until
   the volume is deleted, and every service still reports healthy while serving
   the old realm. This has already cost time once this week. T007 and T012 sidestep
   it with a throwaway container; T014 is the only place it must be faced, and it
   is the first thing that task says.

2. **The convention check is taken to mean more than it does.** It reads names.
   It cannot see a mapper that exists and does not fire, and it cannot see clients
   created at runtime at all — those are not in the file, and research R3 measured
   them as already fine for a reason no file records. T011 exists to make that
   concrete rather than leave it as a caveat, and the contract states it as a
   table row that says **nothing** catches the runtime case.

3. **`preferred_username` disappears and nobody notices until something wants it.**
   Nothing reads it today — verified before the mapper was removed. But it is the
   one thing in this feature that changes what a token *contains* rather than
   where it comes from, and a silent removal is how the next reader loses an hour.
   T005 exists to name it in the record.

---

## What the automated suite does and does not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| Every client in the file holds the identity scope | `RealmIdentityTests` (a) | — |
| Every scope a client names exists | `RealmIdentityTests` (b) | today: a start-up warning |
| No permission carries an identity mapper | `RealmIdentityTests` (c) | — |
| No client carries a private copy | `RealmIdentityTests` (d) | — |
| A credential actually carries the subject | `TokenAttributionIntegrationTests`, by minting one | **`RealmIdentityTests` — it reads names** |
| An attributed write lands attributed | T014, by a person, through the audit trail | the integration test proves only that it is not refused |
| Nothing is discarded on import | T007, by counting | no test asserts this — it is a log line |
| A runtime-created identity can be attributed | **nothing** | both checks — those clients are not in the file |

The last two rows are stated rather than solved. A log line is not an assertion,
and a file cannot describe clients that are not in it.
