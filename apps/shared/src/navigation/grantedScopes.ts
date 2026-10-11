/**
 * Spec 316 FR-009 — the only input to navigation visibility is the OIDC
 * `User.scope` value (the token response's `scope`), never the access token
 * itself (callers do not decode it). Shared so any remote can gate a
 * client-side action the same way the shell gates a nav entry (issue #2855).
 */
export function grantedScopes(user: { readonly scope?: string } | undefined): ReadonlySet<string> {
  return new Set((user?.scope ?? '').split(' ').filter(Boolean));
}
