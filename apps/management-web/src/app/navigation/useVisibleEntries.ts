import { useMemo } from 'react';
import { useAuth } from 'react-oidc-context';
import { grantedScopes } from './grantedScopes.js';
import { visibleEntries, type NavEntry } from './visibleEntries.js';
import { useNavigation } from './NavigationProvider.js';

/**
 * Spec 316 FR-010/FR-011/FR-014 — the combined gate `ShellLayout` (nav bar
 * and command palette) and `router.tsx`'s `<Gated>`/`<FirstVisibleSurface>`
 * all read. Memoised on `user?.scope` (FR-014): a silent token renewal or an
 * OIDC subject change each produce a new `User` object `react-oidc-context`
 * re-renders with, but only a *scope* change can change this function's own
 * output — the one value the computation actually reads — so recomputing on
 * `user?.profile.sub` alone, with the same scope, would just rebuild an
 * identical result.
 */
export function useVisibleEntries(): readonly NavEntry[] {
  const { entries } = useNavigation();
  const { user } = useAuth();
  const scope = user?.scope;

  return useMemo(() => visibleEntries(entries, grantedScopes({ scope })), [entries, scope]);
}
