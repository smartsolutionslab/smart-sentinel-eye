# No contract changes, and one configuration change

**Recorded deliberately**: an empty `contracts/` folder reads as an oversight,
and so does its absence.

## No HTTP or message contract changes

Nothing about what the services accept or return changes. The kiosk already
authenticates and already calls the same endpoints with the same authority; what
changes is how long its proof of identity lasts and where the device keeps it.

## What does change, outside the app

**The kiosk client must be permitted to request a long-lived grant.** The realm
already defines that grant type — it ships with it — but the kiosk client can
neither request it nor receive it by default today. Verified by querying the
client's own scope lists on the running system.

This is configuration, not contract: no caller's request or response shape moves,
and nothing outside this repository consumes it.

## What deliberately does not change

- **Enrolment.** It already mints a per-device identity and reveals its secret
  once. This feature does not use it, does not change it, and records why — the
  secret cannot live where the code that would use it runs.
- **Authority.** The kiosk's scopes are identical before and after. A reviewer
  should be able to confirm that in one diff.
- **The services.** No C# is touched.
