# Spec 129 — Absent is not empty

**Issue:** #2139 (a missing group and an empty one are the same value)
**Branch:** `fix/2139-absent-is-not-empty`
**Status:** Phase 3 complete
**Lane:** autonomous (ADR-0144)
**ADRs:** 0037 (phases), 0144 (lane, 4a colour), 0036 (smallest change),
0141 (`Option<T>` for absence in Domain and Application; NRT),
0048 (`Option<T>` is the absence vocabulary), 0067 (`MigrationRunner`),
0105 (`Ensure.That`), 0049 (`CancellationToken` last), 0050
(`[LoggerMessage]`), 0084 (code metrics), 0052/0053/0054 (xUnit +
Shouldly, sentence-style names, hand-written fakes).
Constitution §II, §Testing.

## The issue's premise is half refuted by the tree, and that is the first finding

#2139 was filed **during #2062's phase 4a/4b**, and describes a tree that
#2062 itself then changed. Two of its three load-bearing claims no longer
hold:

| #2139 says | The tree says |
|---|---|
| "#2062 added a bounded wait to `KeycloakProvisionedFabSource`" | The wait was added in `a7c2b3fb` and **reverted in `0df30ac4`**. There is no wait. |
| "`KeycloakProvisionedFabSourceWaitTests.EventuallyPopulatedKeycloakAdminClient` … the test asserts a wait on exactly that value" | Retired in `56777dbb`. Neither the class nor the fake exists anywhere in the repo, on `develop` or on any pushed branch. |
| "`Throws_when_the_realm_has_no_fab_groups_at_all` is the test that now takes 30 s" | It takes **206 ms**, measured on this branch before any change. `56777dbb` records 27 ms. |

`0df30ac4` and `56777dbb` give the evidence for the revert, from three
Keycloak container boots on this machine:

```
16:07:57.100  KC-SERVICES0032  Import finished successfully
16:07:58.314  io.quarkus       started in 27.154s. Listening on:
                               http://0.0.0.0:8080 and https://0.0.0.0:8443.
                               Management interface listening on https://0.0.0.0:9000.
```

The import finishes **1.2 s before any listener binds**, and 8080, 8443 and
9000 arrive on one line — so nothing that speaks HTTP to Keycloak, token
mint or admin call or health probe, can observe a realm whose groups are not
yet in the model. **The boot race the wait polled for has no reachable
input.** Nothing in `src/` creates a fab group at runtime either; the only
`POST` in `HttpKeycloakAdminClient` is to `admin/realms/{realm}/clients`.

**So this spec does not restore the wait**, and #2139's "done looks like …
`KeycloakProvisionedFabSource` **waits on absent**" is declined with that
evidence rather than implemented. Restoring it would undo a merged decision
on grounds the same decision already disproved, and would reintroduce the
very 30 s the issue complains about.

## What is still true, and is the defect this spec closes

`HttpKeycloakAdminClient.GetSubGroupNamesAsync` still maps **404 → `[]`**
before `EnsureSuccessStatusCode()`. That has two live costs, neither of
which depends on a wait.

**1. The interface's own contract is false.** `IKeycloakAdminClient`
documents:

> Throws when the realm cannot be reached; **never returns empty to mean
> "could not tell"**.

A 404 on `group-by-path/fabs` is exactly "could not tell you about the
children of a group that is not there", and the implementation returns `[]`
for it.

**2. The abort message has to hedge.** `KeycloakProvisionedFabSource`, on
the one verdict left in the system that names two causes:

> `Nothing at all under '/fabs' in the realm: the group is absent, or
> present with no children.`

Those are different faults with different fixes — a realm imported without
the `/fabs` group (or a warm data volume whose realm predates it, which
`Strategy: IGNORE_EXISTING` skips) versus a realm whose `/fabs` group was
imported with no fabs declared under it. The reader is handed the
disjunction and has to resolve it by hand, against a realm. #2062's whole
subject was that a migration abort names its cause; **this is the one place
left where it names two**, and the reason is that the value it reasons over
cannot tell them apart.

## Requirements

- **FR-001** `GetSubGroupNamesAsync` distinguishes **absent** (the parent
  group is not in the realm) from **present-and-childless**.
- **FR-002** Absence is expressed as `Option<T>.None`, not as an empty
  collection and not as `null`.
- **FR-003** "Could not tell" — unreachable realm, 403, 5xx — keeps
  throwing. It is neither `None` nor `Some([])`.
- **FR-004** `KeycloakProvisionedFabSource` gives **three** verdicts, one
  per cause: absent group, childless group, no usable name. Each names a
  different thing to go and look at.
- **FR-005** All three abort. **Nothing becomes more permissive**: a fab
  source that cannot enumerate still refuses rather than provisioning
  nothing and reporting success (spec 019 FR-011, unchanged).
- **FR-006** No wait, no poll, no retry is introduced.
- **FR-007** The existing six `KeycloakProvisionedFabSourceTests` and three
  `MigrationRunTests` stay green **unmodified** across the shape change.

## Success criteria

- **SC-001** A test tells an absent `/fabs` apart from a childless one by
  the verdict each produces, and is red before the fix.
- **SC-002** `dotnet build -c Release` — 0 warnings, 0 errors.
- **SC-003** `Throws_when_the_realm_has_no_fab_groups_at_all` stays in the
  hundreds of milliseconds. It was never 30 s on this tree, so this is a
  guard against reintroducing the wait, not a repair.

## What this is not

- **Not a latency-budget change.** Constitution §IV's six legs run from
  event arrival to overlay rendered. This is startup fab resolution in
  `MigrationRunner`, which has finished before any camera streams. **No leg
  is touched**, and none of the six figures moves.
- **Not a wait.** See above.
- **Not a security change.** Realm and group resolution is read-only here
  and stays read-only. The set of realms, groups and callers is unchanged;
  the only difference is which sentence an abort prints. Every path that
  aborted before still aborts.
