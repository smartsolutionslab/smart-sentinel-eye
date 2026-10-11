# Tasks — Spec 337 (#2797)

- [ ] T001 Split `A_webhook_stamped_client_and_an_unknown_kind_are_never_candidates` into two facts
- [ ] T002 New red fact: a `webhook`-kind orphan past grace, no active row, is now disabled
- [ ] T003 New fact: a `webhook`-kind client WITH an active `RegisteredClient` row is left alone
- [ ] T004 `OrphanedClientSweep.SweptKinds` gains `"webhook"`; doc comments corrected
- [ ] T005 Full `Identity.Application.Tests` green, device/kiosk/unknown-kind facts unmodified
