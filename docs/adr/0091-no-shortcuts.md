# ADR-0091: Extends ADR-0069 — No Shortcuts or Aliases in Names

**Status:** Accepted (extends ADR-0069)
**Date:** 2026-05-25

## Context

ADR-0069 captured a few Yumney style conventions in CONTRIBUTING.md.
A broader rule emerged from the Yumney compare and the user's
guidance: identifier names should spell out the concept, not
abbreviate it.

## Decision

**No shortcuts, no aliases, no abbreviations** in any identifier
(type, property, parameter, local, file name, project name, folder
name).

| Shortcut | Use instead |
|---|---|
| `Id` | `Identifier` |
| `Repo` | `Repository` |
| `Mgr` | `Manager` |
| `Cfg` | `Configuration` |
| `Ctx` | `Context` |
| `Db` | `Database` |
| `Msg` | `Message` |
| `Auth` | `Authentication` or `Authorization` (per case) |
| `Authn` | `Authentication` |
| `Authz` | `Authorization` |
| `Req` | `Request` |
| `Res` / `Resp` | `Response` |
| `Tx` / `Trx` | `Transaction` |
| `Svc` | `Service` |
| `Idx` | `Index` |
| `Tmpl` | `Template` |
| `Param` | `Parameter` (in identifier contexts) |
| `Arg` | `Argument` (in identifier contexts) |
| `Pkg` | `Package` |
| `Cmd` | `Command` |
| `Q` | `Query` |
| `Hdlr` | `Handler` |
| `Evt` / `Ev` | `Event` |
| `Btn` | `Button` |
| `Img` | `Image` |
| `Pwd` | `Password` |

**Industry-standard exceptions** that remain unchanged because they
are universally recognized in context:

- `Url`, `Uri`, `Id` *only* as part of `Guid` (the type itself), `IO`,
  `Json`, `Xml`, `Html`, `Css`, `Api`, `Cli`, `Sdk`, `Dto`, `Cqrs`,
  `Otp`, `Pkce`, `Rbac`, `Abac`, `Cel`, `Ptz`, `Ntp`, `Ptp`, `Sfu`,
  `Rtp`, `Rtsp`, `Onvif`, `Tls`, `Ssl`, `Http`, `Https`, `Tcp`,
  `Udp`, `Dns`.
- `IAuthorizationDecisionPoint` — the industry-standard policy-engine
  term (ADR-0023).
- Loop variable `i`, `j`, `k` and the `_` discard pattern in `lambdas`
  remain idiomatic.

## Consequences

- **Positive:** consistent, searchable, self-documenting names.
- **Positive:** pairs with ADR-0090 (`Identifier` suffix).
- **Negative:** longer names everywhere. Trade-off accepted; the
  hard limits in ADR-0084 keep individual lines bounded.

## Alternatives Considered

- **Allow common shortcuts** (`Id`, `Repo`, `Cmd`) — drift over time;
  reviewers re-litigate per PR.
- **Per-team style** — inconsistent.
