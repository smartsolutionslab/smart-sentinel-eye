# Quickstart: proving it per identity, not per file

**Feature**: 042 | **Date**: 2026-08-26

This feature exists because a configuration file was believed instead of
measured — and the first draft of its own spec made the same mistake. So the
verification is a credential per identity, and nothing here is confirmed by
reading the realm.

Most of it needs no running stack: a throwaway directory service is faster than
the Aspire stack and does not need its volume cleared.

---

## 0. The trap that costs twenty minutes

The development Keycloak is persistent **with a data volume**. The realm imports
only into an empty database, so **editing the file changes nothing until the
volume is deleted** — and the stack boots healthy either way, serving the old
realm.

For everything except step 4, avoid the problem entirely by using a throwaway
container.

---

## 1. Import the realm on its own

```sh
docker run -d --name kc-check -p 8096:8080 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
  -v "<dir containing the realm json>:/opt/keycloak/data/import" \
  quay.io/keycloak/keycloak:26.5 start-dev --import-realm
```

**SC-001** — count what was thrown away:

```sh
docker logs kc-check 2>&1 | grep -c "doesn't exist. Ignoring"
```

**Expected: `0`.** It was **32**. Also check no other import warning appeared:

```sh
docker logs kc-check 2>&1 | grep -i "WARN.*RepresentationToModel\|ERROR"
```

---

## 2. One credential per identity — the whole point

**SC-002.** Not a sample. The two identities that work today do so by accident,
so a sample would probably have picked one.

Most clients have direct access grants disabled, correctly. Enable them **on a
scratch copy of the realm only**, for the user-facing three, and say so in the
note — it is a probe, not a change.

For each of the **user-facing** identities — the operator console, its
replacement, and the wall display — take a token as `operator` / `Operator1234`
and decode the **access** token (not the ID token; that one carries `sub`
regardless, which is why this hid for so long).

For each of the five **background workers**, read its secret through the admin
API and take a `client_credentials` token.

Record, for each of the eight:

| Must be true | Why it is on the list |
|---|---|
| `sub` present | the feature |
| `groups` present where it was before | fab scoping must not regress |
| `scope` identical to before | **SC-007** — permissions do not change here |
| `sse-identity` absent from `scope` | it grants nothing, so it must not read as though it does |

**And one absence to confirm**: `preferred_username` is gone from the operator
console's token. That is the single behavioural change in this feature. Nothing
reads it; confirm it is gone rather than discover it later.

---

## 3. Make each check fail

**SC-003 / SC-004.** A check that has not been seen red is a claim.

1. Remove `sse-identity` from one client → the convention check must fail.
2. Give a client a scope name that does not exist → the convention check must
   fail. **Today this is discarded at start-up with a warning nobody reads**,
   which is the mechanism the whole feature is about.
3. Remove the mapper from `sse-identity`, leaving the scope in place → the
   convention check **passes** and the **integration** test fails.

The third is the interesting one. It shows what each check is worth: the cheap
one reads names, and only a minted token shows behaviour. Record both outcomes.

---

## 4. The one thing that needs the real stack

**SC-005** — an attributed change, end to end.

Delete the Keycloak container **and its volume**, then boot with
`dotnet run --project src/AppHost`. Confirm the import ran
(`KC-SERVICES0030: Full model import requested`) and that no entries were
discarded.

Sign into the operator console, change something — rename a camera, publish a
layout — and confirm it succeeds. A missing subject produces a **401**, not a
wrong value, so success is the assertion.

Then check the audit trail shows the change against the operator who made it.

---

## 5. What to write down

- The `0` from step 1, and the `32` it replaces.
- All eight token payloads from step 2, verbatim — this is the evidence, and
  summarising it is how the first draft went wrong.
- The absence of `preferred_username`.
- Both check failures from step 3, including which check did **not** fire.
- The attributed change from step 4.

If a step was not performed, **say which**. The point of this feature is that a
file said something nobody had checked.
