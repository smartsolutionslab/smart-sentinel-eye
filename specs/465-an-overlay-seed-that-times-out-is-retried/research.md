# Research — spec 465

No `NEEDS CLARIFICATION` remains in the plan; these are the choices that had alternatives.

## Where the retry lives
- **Decision:** inside `ReverseIndexSeederHostedService`, as a `BackgroundService` loop.
- **Why:** it keeps the registration pinned by `SystemVariablesRegistrationCharacterisationTests`
  (`IHostedService → ReverseIndexSeederHostedService`) and the named client with its auth handler.
- **Rejected:** a second Polly pipeline on the client. ADR-0143 already retries `GET` inside one attempt and
  the 30 s timeout cuts that short; a longer pipeline timeout would make `StartAsync` wait longer, which
  FR-003 forbids. Retry must be outside the HTTP call.

## Backoff
- **Decision:** 2 s initial, ×2, cap 30 s, unbounded attempts, no jitter.
- **Why:** an unbounded loop is the point (the spec's first assumption); one instance per pod, so no herd.
- **Rejected:** a fixed interval (hammers a down service at the rate that suits a slow one); a bounded
  attempt count (recreates the defect after a long outage).

## Time seam
- **Decision:** inject `TimeProvider`, use `Task.Delay(delay, timeProvider, ct)`.
- **Why:** `TimeProvider.System` is already a singleton in this module; a hand-written `ManualTimeProvider`
  already exists in three test projects (ADR-0054: `FakeTimeProvider` is not referenced).
- **Rejected:** a custom `IDelay` interface (a second seam for the same thing).

## Race between listing and events (FR-006)
- **Decision:** a seed-only index write that skips overlays touched by an event, tombstones included.
- **Why:** the only state that distinguishes "archived by an event" from "never seen" is a record that an
  event happened; absence from `labelsByOverlay` cannot tell them apart.
- **Rejected:** "seed only if the index is empty" (an event for another overlay would block the whole
  seed); version-comparing the listing against events (the listing carries a revision, the index does not).

## Refused seed (FR-008)
- **Decision:** retried, logged at `Error`, wording changed. Supersedes spec 126's one-shot for that case.
- **Why:** a corrected credential then works without a restart, and a Keycloak that comes up late is
  the same recovery as an overlay-designer that comes up late.
