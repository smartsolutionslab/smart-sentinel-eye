/**
 * Spec 316 FR-009 — the only input to navigation visibility is the OIDC
 * `User.scope` value (the token response's `scope`), never the access token
 * itself (the shell does not decode it).
 */
export function grantedScopes(user: { readonly scope?: string } | undefined): ReadonlySet<string> {
  return new Set((user?.scope ?? '').split(' ').filter(Boolean));
}
