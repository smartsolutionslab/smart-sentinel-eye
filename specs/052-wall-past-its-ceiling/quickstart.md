# Quickstart — 052 a wall past its ceiling

How to see the problem, how to check the containment, and what none of it
establishes.

---

## Seeing the problem

The privilege is inert today — no client offers the scope — so the problem is
visible only by asking the provider who *holds* it.

```sh
# the dev provider is usually already up, outliving the AppHost
docker ps --filter name=keycloak
```

Admin credentials are in the container's environment
(`KC_BOOTSTRAP_ADMIN_PASSWORD`), and it is **not** `admin`. The provider serves
HTTPS with a development certificate, so Node needs certificate checking
disabled and browser contexts need `ignoreHTTPSErrors`.

Enrol a kiosk, then ask what its service account effectively holds:

```
GET /admin/realms/{realm}/users/{id}/role-mappings/realm/composite
```

Before this feature it answers `default-roles-…, offline_access,
uma_authorization, user`. **`offline_access` is the privilege**, and every
account created after import has it.

Compare with `operator`, imported from the realm file: `user` alone. That
difference — declared versus created — is the whole defect.

---

## Checking the containment

```
GET    /admin/realms/{realm}/users/{id}/role-mappings/realm    → the direct list
DELETE /admin/realms/{realm}/users/{id}/role-mappings/realm    → that same list
```

**Send back exactly what the GET returned.** A role object obtained any other way
returns **404**, which reads like a permission problem and is not — that cost a
cycle during planning.

Afterwards the composite call returns nothing, and the kiosk can still obtain a
token. Running it twice is fine.

---

## What "done" looks like, per story

| Story | Done when | Not done merely because |
|---|---|---|
| **US1** | the running provider says an enrolled kiosk holds nothing, an operator holds nothing, a wall display holds it | the realm file lists four wall accounts |
| **US2** | a wall screen's refresh token decodes as an **offline** grant with **no expiry** | a token exists |
| **US3** | every scope in the wall token is exercised and `sse.events.write` is absent | three endpoints returned 403 |
| **US4** | **not built** | — |

---

## The shortened-ceiling run — gated, and read carefully

Nothing in CI runs for ten hours, so surviving the ceiling in real time needs the
ceiling shortened on a test realm.

**Spec 050 did this and it broke the e2e seeds**: the seeds drive a long operator
session which then expired mid-run. It appeared to work only because the dev
database already held published layouts — a caveat, not a recipe.

So this run is **gated behind an explicit flag**, never part of the default
suite, and:

> **It demonstrates the mechanism, not the production configuration.**

That sentence belongs in the task, in the test, and in the verification note.
Spec 050 put it in three places and the note still had to be corrected
afterwards, so three is the floor rather than the target.

---

## What a fully green suite will still not establish

- **Twenty screens.** Four is the most ever exercised, once, in spec 051.
- **A real power cut.** A reload is not a power cut.
- **Ten hours in production.** A shortened ceiling shows the mechanism.
- **That an account created by hand is contained.** It is not; filed (FR-002a).
- **That anything rotates a wall-display credential.** Nothing does.
- **Anything about production.** There is none (ADR-0130), and `deploy/`
  provisions no realm — so whoever builds one must carry the wall accounts, the
  wall client **and** the containment. Having the client without the containment
  is worse than having neither.

---

## Environment notes that have already cost time

- The provider takes ~20 s to serve after `docker start`; wait for the realm's
  discovery document rather than sleeping a fixed amount.
- A restarted provider keeps its old realm — a realm-file edit does not appear
  without deleting the volume.
- Containers outlive the AppHost, so the provider is often already running while
  nothing else is.
- Editing the realm file: **line by line, never reserialised.** A round-trip
  through a JSON writer expands its compact arrays and turns a ninety-line change
  into four hundred.
