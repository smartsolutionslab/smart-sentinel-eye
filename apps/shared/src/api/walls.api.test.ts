import { describe, it, expect } from 'vitest';
import { wallsApi } from './walls.api.js';
import { createWallSchema } from './walls.schema.js';

/**
 * Spec 258 US1, T014 (tasks.md). `walls.api.ts` and `walls.schema.ts` do not
 * exist yet — this file is RED on import failure alone, which is the
 * expected/acceptable form of "red for missing types" (T016). Once the
 * production files exist, these assertions pin:
 *
 * - the switch/edit-scenes mutations send `If-Match` built from the cached
 *   wall's `version` (ADR-0113, mirroring `layouts.api.ts`'s `publishRevision`);
 * - `createWallSchema` accepts 2..8 distinct scene identifiers and rejects
 *   too few, too many, or a duplicate (PD-5, mirroring `layouts.schema.ts`'s
 *   grid/tile refinements).
 *
 * Naming assumption (stated per the brief): the gateway exposes a dedicated
 * `/walls/{**catch-all}` route (plan.md §4.5, tasks.md T002) rather than
 * routing walls under `/layout-composition/...` the way `layouts.api.ts`
 * does — so `walls.api.ts` is assumed to use `gatewayBaseQuery('walls')`,
 * not `gatewayBaseQuery('layout-composition/walls')`. If the implementing
 * engineer chooses the latter instead, only the URL assertions below need
 * adjusting; the shape of the assertions (If-Match present, body shape) does
 * not depend on the choice.
 */
describe('wallsApi', () => {
  it('sends If-Match built from the wall version when switching scenes', () => {
    const args = wallsApi.endpoints.switchWallScene.query({
      wallIdentifier: 'w-1',
      version: 3,
      target: 'next',
    });

    expect(args.method).toBe('POST');
    expect(args.url).toContain('w-1');
    expect(args.url).toContain('switch');
    expect(args.headers).toMatchObject({ 'If-Match': '"3"' });
  });

  it('sends If-Match built from the wall version when editing scenes', () => {
    const args = wallsApi.endpoints.editWallScenes.query({
      wallIdentifier: 'w-1',
      version: 5,
      scenes: ['a', 'b'],
    });

    expect(args.method).toBe('PUT');
    expect(args.url).toContain('w-1');
    expect(args.url).toContain('scenes');
    expect(args.headers).toMatchObject({ 'If-Match': '"5"' });
    expect(args.body).toMatchObject({ scenes: ['a', 'b'] });
  });

  it('a jump-to-scene switch carries the target layout in the body', () => {
    const args = wallsApi.endpoints.switchWallScene.query({
      wallIdentifier: 'w-1',
      version: 0,
      target: 'layout',
      layout: 'scene-b',
    });

    expect(args.body).toMatchObject({ target: 'layout', layout: 'scene-b' });
  });
});

describe('createWallSchema', () => {
  const GUID_A = '11111111-1111-1111-1111-111111111111';
  const GUID_B = '22222222-2222-2222-2222-222222222222';
  const GUID_C = '33333333-3333-3333-3333-333333333333';

  it('accepts a name and 2 distinct scenes', () => {
    const result = createWallSchema.safeParse({ name: 'Line 3 rotation', scenes: [GUID_A, GUID_B] });
    expect(result.success).toBe(true);
  });

  it('accepts the maximum of 8 distinct scenes', () => {
    const scenes = Array.from({ length: 8 }, (_, index) => `${index}${GUID_A.slice(1)}`);
    const result = createWallSchema.safeParse({ name: 'Eight scenes', scenes });
    expect(result.success).toBe(true);
  });

  it('rejects a single scene (PD-5 MinScenes)', () => {
    const result = createWallSchema.safeParse({ name: 'Too few', scenes: [GUID_A] });
    expect(result.success).toBe(false);
  });

  it('rejects nine distinct scenes (PD-5 MaxScenes)', () => {
    const scenes = Array.from({ length: 9 }, (_, index) => `${index}${GUID_A.slice(1)}`);
    const result = createWallSchema.safeParse({ name: 'Too many', scenes });
    expect(result.success).toBe(false);
  });

  it('rejects a duplicate scene', () => {
    const result = createWallSchema.safeParse({ name: 'Dup', scenes: [GUID_A, GUID_A, GUID_C] });
    expect(result.success).toBe(false);
  });

  it('rejects a blank name', () => {
    const result = createWallSchema.safeParse({ name: '  ', scenes: [GUID_A, GUID_B] });
    expect(result.success).toBe(false);
  });
});
