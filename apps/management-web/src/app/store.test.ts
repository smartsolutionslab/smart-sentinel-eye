import { describe, expect, it } from 'vitest';
import { camerasApi, type CameraDetail } from '@smart-sentinel-eye/shared/api/cameras.api';
import { streamsApi, type StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import { layoutsApi, type Layout } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi, type Overlay } from '@smart-sentinel-eye/shared/api/overlays.api';
import { rulesApi, type Rule, RULE_ACTION_SET_VARIABLE_VALUE } from '@smart-sentinel-eye/shared/api/rules.api';
import { systemVariablesApi, type Variable } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { auditApi, type AuditRow } from '@smart-sentinel-eye/shared/api/audit.api';
import { wallsApi, type Wall } from '@smart-sentinel-eye/shared/api/walls.api';
import { store, apiSlices, resetApiCaches } from './store.js';

/**
 * Spec 303 (#2524) T001 — RED.
 *
 * `apiSlices` and `resetApiCaches` do not exist on `store.ts` yet (plan.md
 * §1, §4 test 2) — both named imports below are expected to fail: `store.ts`
 * itself exists, so this is the "missing named export" case, not a missing
 * module. `apiSlices`/`resetApiCaches` typecheck as errors under `tsc
 * --noEmit` (TS2305), and resolve to `undefined` at Vitest's runtime (esbuild
 * does not enforce named-export existence the way real ESM/tsc does) — so
 * each test below fails with a `TypeError` the first time it touches one of
 * them, not with a suite-level import failure. Both are genuine, acceptable
 * compile-era red for the right reason (FR-005's foundation is missing).
 */

const ONE_QUERY_SEEDED = 'camera-1';

const cameraFixture: CameraDetail = {
  cameraIdentifier: 'camera-1',
  version: 1,
  fab: 'fab-1',
  name: 'Camera One',
  rtspUrl: 'rtsp://camera-1',
  registeredAt: '2026-01-01T00:00:00Z',
  status: 'Registered',
};

const streamFixture: StreamHealth = {
  cameraIdentifier: 'camera-1',
  state: 'Healthy',
  whepUrl: 'https://stream.test/camera-1',
  transcodeMode: 'Passthrough',
  lastSuccessAt: '2026-01-01T00:00:00Z',
  error: null,
};

const layoutFixture: Layout = {
  layoutIdentifier: 'layout-1',
  version: 1,
  fab: 'fab-1',
  name: 'Layout One',
  createdAt: '2026-01-01T00:00:00Z',
  createdBy: 'operator',
  revisions: [],
};

const overlayFixture: Overlay = {
  overlayIdentifier: 'overlay-1',
  version: 1,
  name: 'Overlay One',
  createdAt: '2026-01-01T00:00:00Z',
  createdBy: 'operator',
  revisions: [],
};

const ruleFixture: Rule = {
  version: 1,
  ruleIdentifier: 'rule-1',
  fab: 'fab-1',
  name: 'rule-1',
  triggerSource: 'SystemVariable',
  triggerKind: 'ValueChanged',
  predicate: 'true',
  action: {
    kind: RULE_ACTION_SET_VARIABLE_VALUE,
    variableName: 'variable-1',
    valueExpression: '1',
    overlay: null,
    durationMs: null,
    wall: null,
    sceneTarget: null,
    targetLayout: null,
  },
  state: 'Draft',
  createdAt: '2026-01-01T00:00:00Z',
  createdBy: 'operator',
  publishedAt: null,
  archivedAt: null,
};

const variableFixture: Variable = {
  variableIdentifier: 'variable-1',
  version: 1,
  fab: 'fab-1',
  name: 'variable-1',
  type: 'String',
  state: 'Defined',
  value: '1',
  truthyLabel: null,
  falsyLabel: null,
  createdAt: '2026-01-01T00:00:00Z',
  createdBy: 'operator',
};

const auditFixture: AuditRow = {
  auditIdentifier: 'audit-1',
  occurredAt: '2026-01-01T00:00:00Z',
  receivedAt: '2026-01-01T00:00:00Z',
  fab: 'fab-1',
  eventKind: 'CameraRegistered',
  resourceKind: 'Camera',
  resourceIdentifier: 'camera-1',
  actorIdentifier: 'operator',
  actorIsSystem: false,
  actorUsername: 'operator',
  eventIdentifier: 'event-1',
  payload: '{}',
  payloadSizeBytes: 2,
  schemaVersion: 1,
};

const wallFixture: Wall = {
  wallIdentifier: 'wall-1',
  version: 1,
  fab: 'fab-1',
  name: 'Wall One',
  scenes: ['scene-1'],
  showing: 'scene-1',
  sceneVersion: 1,
  showingSince: '2026-01-01T00:00:00Z',
};

function queriesFor(reducerPath: string): Record<string, unknown> {
  const state = store.getState() as unknown as Record<string, { queries: Record<string, unknown> }>;
  return state[reducerPath]!.queries;
}

describe('store.ts — the apiSlices/resetApiCaches contract (spec 303, #2524)', () => {
  it('apiSlices names exactly the reducers mounted in the store (FR-005 drift guard)', () => {
    const mountedReducerPaths = Object.keys(store.getState()).sort();
    const declaredReducerPaths = apiSlices.map((slice) => slice.reducerPath).sort();

    // A 9th `createApi` slice added to `reducer`/`middleware` but not to
    // `apiSlices` would mismatch here, and this is exactly the test that is
    // supposed to catch it (FR-005) — it fails the same way if `apiSlices`
    // under-counts today's eight.
    expect(declaredReducerPaths).toEqual(mountedReducerPaths);
  });

  it('resetApiCaches empties every slice that was seeded with cached data', () => {
    store.dispatch(camerasApi.util.upsertQueryData('getCamera', { cameraIdentifier: ONE_QUERY_SEEDED }, cameraFixture));
    store.dispatch(streamsApi.util.upsertQueryData('getStream', 'camera-1', streamFixture));
    store.dispatch(layoutsApi.util.upsertQueryData('getLayout', 'layout-1', layoutFixture));
    store.dispatch(overlaysApi.util.upsertQueryData('getOverlay', 'overlay-1', overlayFixture));
    store.dispatch(rulesApi.util.upsertQueryData('getRule', { name: 'rule-1' }, ruleFixture));
    store.dispatch(systemVariablesApi.util.upsertQueryData('getVariable', { name: 'variable-1' }, variableFixture));
    store.dispatch(auditApi.util.upsertQueryData('getAuditEvent', 'audit-1', auditFixture));
    store.dispatch(wallsApi.util.upsertQueryData('getWall', 'wall-1', wallFixture));

    // Sanity: every slice actually holds the entry just seeded, so the
    // emptiness asserted below is `resetApiCaches`'s doing, not an artefact of
    // nothing having been cached in the first place.
    for (const slice of apiSlices) {
      expect(Object.keys(queriesFor(slice.reducerPath))).toHaveLength(1);
    }

    resetApiCaches(store.dispatch);

    for (const slice of apiSlices) {
      expect(queriesFor(slice.reducerPath)).toEqual({});
    }
  });
});
