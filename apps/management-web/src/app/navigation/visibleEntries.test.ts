import { describe, expect, it } from 'vitest';
import { grantedScopes } from './grantedScopes.js';
import { visibleEntries, type NavEntry } from './visibleEntries.js';

/**
 * Spec 316 (issue superseding #1008) T010 — RED (ADR-0139/0144), plan.md §4.3
 * / §6 test 2. Neither `./visibleEntries.ts` nor `./grantedScopes.ts` exists
 * yet (T011, US2, creates both). Every case here fails on a missing module
 * today, not on an assertion mismatch against a working filter.
 *
 * The fixture mirrors tasks.md's seven-entry nav exactly as plan.md §4.3
 * fixes it: the six in-shell entries keep today's `DESTINATIONS` order at
 * 20, 30, …, 70, and the cameras remote's manifest-declared entry sits at
 * `order: 10` — so a full-scope session sees today's exact order
 * (Cameras, Layouts, Walls, Overlays, Rules, System variables, Audit).
 *
 * The four scope sets are spec.md US2's independent test (§2, "Independent
 * test"): all 21 console scopes; the same minus `sse.cameras.read`; only
 * `sse.audit.read`; empty.
 */

const ALL_21_SCOPES = [
  'sse.variables.write',
  'sse.identity.kiosks.read',
  'sse.identity.devices.read',
  'sse.audit.read',
  'sse.cameras.read',
  'sse.overlays.read',
  'sse.identity.devices.write',
  'sse.rules.read',
  'sse.overlays.write',
  'sse.events.read',
  'sse.streams.write',
  'sse.layouts.write',
  'sse.variables.read',
  'sse.events.write',
  'sse.identity.kiosks.write',
  'sse.rules.write',
  'sse.webhooks.write',
  'sse.cameras.write',
  'sse.streams.read',
  'sse.events.types.write',
  'sse.layouts.read',
].join(' ');

const WITHOUT_CAMERAS_READ = ALL_21_SCOPES.split(' ')
  .filter((scope) => scope !== 'sse.cameras.read')
  .join(' ');

/** Mirrors plan.md §3's "Entry" table (FR-010) and §4.3's order values. */
const FIXTURE_ENTRIES: readonly NavEntry[] = [
  { path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] },
  { path: '/layouts', label: 'Layouts', order: 20, requiredScopes: ['sse.layouts.read'] },
  { path: '/walls', label: 'Walls', order: 30, requiredScopes: ['sse.layouts.read'] },
  { path: '/overlays', label: 'Overlays', order: 40, requiredScopes: ['sse.overlays.read'] },
  { path: '/rules', label: 'Rules', order: 50, requiredScopes: ['sse.rules.read'] },
  { path: '/system-variables', label: 'System variables', order: 60, requiredScopes: ['sse.variables.read'] },
  { path: '/audit', label: 'Audit', order: 70, requiredScopes: ['sse.audit.read'] },
];

function labelsOf(entries: readonly NavEntry[]): string[] {
  return entries.map((entry) => entry.label);
}

describe('visibleEntries (spec 316 FR-010/FR-011, plan.md §4.3)', () => {
  it('A full-scope session (all 21 sse.* scopes) sees all seven entries in today’s order', () => {
    const scopes = grantedScopes({ scope: ALL_21_SCOPES });

    const result = visibleEntries(FIXTURE_ENTRIES, scopes);

    expect(labelsOf(result)).toEqual(['Cameras', 'Layouts', 'Walls', 'Overlays', 'Rules', 'System variables', 'Audit']);
  });

  it('A session missing sse.cameras.read sees every entry except Cameras', () => {
    const scopes = grantedScopes({ scope: WITHOUT_CAMERAS_READ });

    const result = visibleEntries(FIXTURE_ENTRIES, scopes);

    expect(labelsOf(result)).toEqual(['Layouts', 'Walls', 'Overlays', 'Rules', 'System variables', 'Audit']);
  });

  it('A session holding only sse.audit.read sees only Audit', () => {
    const scopes = grantedScopes({ scope: 'sse.audit.read' });

    const result = visibleEntries(FIXTURE_ENTRIES, scopes);

    expect(labelsOf(result)).toEqual(['Audit']);
  });

  it('A session with no granted scopes sees nothing', () => {
    const scopes = grantedScopes({ scope: '' });

    const result = visibleEntries(FIXTURE_ENTRIES, scopes);

    expect(result).toEqual([]);
  });

  it('An entry requiring more than one scope needs every one of them (all-of)', () => {
    const multiScopeEntries: readonly NavEntry[] = [
      { path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read', 'sse.cameras.write'] },
    ];

    expect(labelsOf(visibleEntries(multiScopeEntries, grantedScopes({ scope: 'sse.cameras.read' })))).toEqual([]);
    expect(
      labelsOf(visibleEntries(multiScopeEntries, grantedScopes({ scope: 'sse.cameras.read sse.cameras.write' }))),
    ).toEqual(['Cameras']);
  });

  it('Entries tied on the same order keep the order they were given in', () => {
    const tiedEntries: readonly NavEntry[] = [
      { path: '/b', label: 'Second-configured', order: 10, requiredScopes: ['sse.audit.read'] },
      { path: '/a', label: 'First-configured', order: 10, requiredScopes: ['sse.audit.read'] },
    ];

    const result = visibleEntries(tiedEntries, grantedScopes({ scope: 'sse.audit.read' }));

    expect(labelsOf(result)).toEqual(['Second-configured', 'First-configured']);
  });
});

describe('grantedScopes (spec 316 FR-009, plan.md §4.3)', () => {
  it('Reads only User.scope, splitting on spaces', () => {
    const scopes = grantedScopes({ scope: 'sse.cameras.read sse.audit.read' });

    expect(scopes.has('sse.cameras.read')).toBe(true);
    expect(scopes.has('sse.audit.read')).toBe(true);
    expect(scopes.size).toBe(2);
  });

  it('Returns an empty set for an undefined user', () => {
    const scopes = grantedScopes(undefined);

    expect(scopes.size).toBe(0);
  });

  it('Returns an empty set for a user with no scope string', () => {
    const scopes = grantedScopes({});

    expect(scopes.size).toBe(0);
  });

  it('Ignores repeated whitespace rather than producing empty tokens', () => {
    const scopes = grantedScopes({ scope: 'sse.cameras.read  sse.audit.read' });

    expect(scopes.size).toBe(2);
    expect(scopes.has('')).toBe(false);
  });
});
