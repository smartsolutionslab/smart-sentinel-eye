import type { NavEntry } from '@smart-sentinel-eye/shared/navigation/navManifest';

export type { NavEntry };

/**
 * Spec 316 FR-010/FR-011, plan.md §4.3 — the visible set is a pure function
 * of the manifests seen so far and the session's granted scopes. An entry is
 * visible iff the granted set holds EVERY one of its `requiredScopes`
 * (all-of), and the result is sorted by `order`. `Array.prototype.sort` is
 * stable (ES2019+), so entries tied on the same `order` keep the order they
 * were given in without an explicit tiebreak.
 */
export function visibleEntries(entries: readonly NavEntry[], scopes: ReadonlySet<string>): readonly NavEntry[] {
  return entries
    .filter((entry) => entry.requiredScopes.every((scope) => scopes.has(scope)))
    .sort((a, b) => a.order - b.order);
}
