import type { NavEntry } from '@smart-sentinel-eye/shared/navigation/navManifest';

/**
 * Spec 316 FR-010's table — the six in-shell nav entries, typed the same way
 * a remote's published manifest entries are, so `NavigationProvider` can
 * gate them through the identical rule (FR-010 rationale: one navigation
 * rule, not one per origin of the code).
 *
 * `order` keeps today's `DESTINATIONS` sequence (20, 30, … 70); the cameras
 * remote's manifest declares `order: 10`, so a full-scope session still sees
 * today's exact order (Cameras, Layouts, Walls, Overlays, Rules, System
 * variables, Audit).
 */
export const shellEntries: readonly NavEntry[] = [
  { path: '/layouts', label: 'Layouts', order: 20, requiredScopes: ['sse.layouts.read'] },
  { path: '/walls', label: 'Walls', order: 30, requiredScopes: ['sse.layouts.read'] },
  { path: '/overlays', label: 'Overlays', order: 40, requiredScopes: ['sse.overlays.read'] },
  { path: '/rules', label: 'Rules', order: 50, requiredScopes: ['sse.rules.read'] },
  { path: '/system-variables', label: 'System variables', order: 60, requiredScopes: ['sse.variables.read'] },
  { path: '/audit', label: 'Audit', order: 70, requiredScopes: ['sse.audit.read'] },
];
