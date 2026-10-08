# ADR-0168: Operator MFE composition is Vite Module Federation, with claims-driven navigation

**Status:** Accepted
**Date:** 2026-10-08
**Extends:** ADR-0107 (operator UI micro-frontends)
**Amends:** ADR-0107 (resolves its deliberately open composition-mechanism question; adds a navigation requirement ADR-0107 did not specify)
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0107 decided to re-architect `management-web` as a shell + per-bounded-context
remotes, but left the composition mechanism open pending a spike (#1007: build a
thin shell + one real remote under each candidate — Module Federation, Web
Components, route-level integration — and measure bundle/load cost,
shared-dependency handling, auth/session sharing, and DX), calling it "the
highest-risk, hardest-to-reverse choice" that "should be decided on evidence, not
up front."

The user made this decision directly, bypassing the spike, when asked explicitly
whether to run it first or decide now (2026-10-08). This ADR records that
decision honestly: **it is not evidence-based.** The spike's purpose — surfacing
real bundle/load/DX costs before committing — has not been served. The risk
ADR-0107 flagged stands; it has been accepted rather than retired by measurement.

Separately, the user's framing of the story added a requirement ADR-0107 did not
contain: the shell's **navigation must be computed from the authenticated
operator's rights/roles/claims**, not shown unconditionally. Today's
`management-web` has no claims-filtered navigation at all — `router.tsx` defines
routes with no scope check, and `auth.ts` requests only the `openid` scope
(the granular `sse.*` scopes and `sse-groups` are default client scopes Keycloak
applies regardless). A user currently sees every route regardless of which
`sse.*` scopes their token actually carries; broken-permission failures surface
only when an API call 403s, not in what navigation offers.

## Decision

1. **Composition mechanism: Vite Module Federation** (`@module-federation/vite`),
   decided directly rather than via #1007's spike. This is the heaviest of the
   three options ADR-0107 named — true runtime composition of independently
   built/deployed bundles, sharing a federated dependency graph (React, RTK,
   the design system) at runtime rather than at build time or via iframe/custom-element
   boundaries.
2. **The shell owns**: routing, auth/session (`react-oidc-context`, ADR-0080),
   the shared design system (ADR-0077/0078), the shared RTK Query/store contract
   (ADR-0075), and the **navigation manifest** (below). It federates in feature
   remotes, lazy-loaded on navigation.
3. **Remotes, one per bounded context** (boundary granularity reaffirmed from
   ADR-0107 at context granularity, not finer — `cameras`, `layouts`, `overlays`,
   `rules`/Automation, `systemVariables`, `walls`, `audit`, mapping onto
   `management-web`'s current `src/features/*` folders), each independently
   built, versioned, and deployable — this is what "start/stop modules
   separately" means operationally: a remote's own build and deploy do not
   require rebuilding the shell or any other remote.
4. **Navigation is claims-driven**: each remote exposes a small, statically
   readable **nav manifest** (route path, label, icon, and the `sse.*` scope(s)
   required to see it) that the shell reads at composition time. The shell
   renders a navigation entry only for a remote whose required scope(s) are
   present in the authenticated session's token (the `sse.*` / `sse-groups`
   claims already issued by Keycloak per fab, ADR-0159). A remote the operator
   has no scope for is not fetched, not federated, and not shown — not merely
   hidden by CSS. This replaces today's unconditional route list in
   `management-web/src/app/router.tsx`.
5. **`kiosk-web` is unaffected** — reaffirming ADR-0107 and ADR-0074. It stays a
   single, separately-deployed display app; it is not a remote and does not
   federate into the operator shell.
6. **Migrate incrementally**, per ADR-0107's own instruction: shell + one real
   remote first (the thin slice #1008 already describes), then the remaining
   bounded contexts, one at a time. Do not big-bang the rewrite of
   `management-web`.

## Consequences

**Positive:**

- Resolves #1007/#1008's block — the mechanism question that stopped both is
  now answered, so #1008-class work (shell + first remote) can proceed through
  the normal spec/plan/tasks phases.
- Claims-driven navigation closes a real gap: today a user can navigate to a
  route their token has no scope for and only discover the failure at the API
  call. The nav manifest makes "can't see it" the enforcement point for
  navigation, with the API's own `RequireScope` checks (constitution §IX)
  remaining the actual authorization boundary — navigation visibility is a UX
  improvement, not a new trust boundary.
- Independent build/deploy per bounded context, as ADR-0107 intended.

**Negative — carried over from ADR-0107, now committed without the spike's evidence:**

- Shared-dependency versioning (React, RTK, design system) across shell and
  every remote must be actively governed via Module Federation's shared-module
  config, or a version mismatch breaks at runtime rather than at build time —
  exactly the risk class the spike existed to measure and has not measured.
  **This is the open risk this ADR accepts rather than resolves.**
- Design system, auth/session, and the RTK contract become shared packages with
  their own release discipline (ADR-0107 Implementation Notes already named
  this).
- Cross-remote navigation, state, and end-to-end testing across remotes are
  materially harder than the current single SPA with internal role-based
  routing.
- Module Federation's runtime composition is the most operationally complex of
  the three candidates ADR-0107 named (versus Web Components' stronger
  isolation or route-integration's simplicity) — the heaviest option was picked
  without the measurement that would have confirmed it was warranted.

## Alternatives Considered

Carried over from ADR-0107, now settled against Module Federation rather than
left open:

- **Web Components** — stronger isolation (each remote ships its own framework
  runtime if needed, no shared-dependency version coupling), framework-agnostic
  shell. Rejected by direct decision, not by measurement — would have been the
  lower-risk choice per ADR-0107's own risk framing.
- **Route-level integration** — each remote is its own deployable app behind
  the shell's routing, simplest to reason about, least "true" runtime
  composition. Rejected by direct decision, not by measurement — would have
  been the simplest choice to implement and operate.
- **Running the spike first** (#1007 as originally scoped) — the
  evidence-gathering path ADR-0107 called for. Explicitly declined in favour of
  deciding now; the user made this call directly when asked.
- **Keep per-SPA role-based routing, add a claims filter without an MFE
  rewrite** — would have closed the navigation gap alone, far cheaper, but
  does not deliver the independent-build/deploy goal ADR-0107 exists for. Not
  what was asked for.

## Implementation Notes

- **Unblocks #1008** (shell host + first feature remote, thin slice) through
  the normal workflow — it needs its own Specify/Plan/Tasks pass now that the
  mechanism is fixed, not a direct implementation; the nav-manifest requirement
  above is new scope #1008's original body did not contain.
- **#1007 is superseded by this ADR**, not executed as scoped — the spike it
  describes was deliberately bypassed. Close it with a reference to this ADR
  rather than running it.
- **Nav manifest shape** (exact schema — e.g. a `NAV_MANIFEST` export per
  remote vs. a shell-side static registry keyed by remote) is implementation
  detail for the Plan phase of the shell+first-remote spec, not fixed here.
- **Shared-dependency governance** (Module Federation's `shared` config:
  singleton React/RTK, version ranges) must be explicit from the first remote,
  not deferred — the risk this ADR accepts without the spike's measurement is
  exactly a shared-dependency break, so the first slice is also where that risk
  gets its first real exercise.
- Served behind ADR-0106's gateway/static edge, consistent with ADR-0107.
