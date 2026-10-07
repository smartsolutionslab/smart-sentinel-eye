# ADR-0073: Integration Event Versioning — Explicit V&lt;N&gt; Suffix

**Status:** Accepted (note 2026-10-07)
**Date:** 2026-05-25

## Context

ADR-0040 separates domain events from integration events; integration
events live in `Shared.Contracts` and are consumed by other contexts.
Schema evolution must not break consumers; explicit versioning makes
the policy clear.

## Decision

- **Every integration event carries an explicit `V<N>` suffix.**
  Initial version is `V1`.

  ```csharp
  public sealed record CameraRegisteredV1(
      CameraId Id,
      CameraName Name,
      RtspUrl Url) : IIntegrationEvent;
  ```

- **Breaking changes** (field removed, type changed, semantic shift)
  bump to `V<N+1>`. The old version remains publishable and consumable
  through a **deprecation window of 1 minor release (~3 months)**.

  ```csharp
  // during the deprecation window
  await bus.PublishAsync(new CameraRegisteredV1(id, name, url), ct);
  await bus.PublishAsync(new CameraRegisteredV2(id, name, url, profile), ct);
  ```

- After the window, `V<N>` is removed in a coordinated PR (publisher
  removes emission; consumers remove their handlers).
- **Additive non-breaking changes** (a new optional field with a safe
  default that consumers can ignore) do NOT bump the version.

> **2026-10-07.** The deprecation window above assumes a consumer outside
> this repo, which cannot be coordinated with directly. **A version whose
> every consumer is in-repo may be cut and deleted in the same commit as
> its successor — no dual-publish, no deprecation window.** This is not a
> new policy; it records what this repo has already done three times
> without an ADR: ADR-0112 §3 states it for multi-tile layouts ("V1 is
> removed in the same feature… there is no dual-publish"), spec 150
> FR-009 cut `ResolvedOverlayTextChangedV1`→`V2` this way, and spec
> 300/ADR-0165:65 cut `OverlayRevisionPublishedV3` the same way. Spec
> 301/#2720 relied on the same reasoning rather than this ADR's literal
> text, which is the gap this note closes. The deprecation-window path
> above remains the rule whenever any consumer is outside this repo.

## Consequences

- **Positive:** explicit upgrade path; no silent contract breakage.
- **Positive:** consumers migrate on their own schedule within the
  window.
- **Negative:** two emissions during deprecation. Acceptable.
- **Negative:** developers must track the deprecation calendar per
  event.

## Alternatives Considered

- **Additive-only evolution** — breaks down when semantics change.
- **Schema registry** — overkill for our event volume.
- **No versioning, just don't break things** — invites accidental
  breakage.
