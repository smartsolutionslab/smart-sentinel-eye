import { describe, expect, it } from 'vitest';
import { jsonResponse, problemResponse } from './responses.js';

/**
 * Spec 468 (#3196) FR-001/FR-002. These helpers replace 22 byte-identical local `jsonResponse`
 * copies and 6 `problemResponse` copies across the apps' tests; the assertions below pin exactly
 * the observable shape those copies shared, so a migrated test sees the same `Response`.
 */
describe('jsonResponse', () => {
  it('defaults the status to 200 when only a body is given', () => {
    expect(jsonResponse({ ok: true }).status).toBe(200);
  });

  it('carries the status it is given', () => {
    expect(jsonResponse({ title: 'gone' }, 404).status).toBe(404);
  });

  it('serialises the body as JSON text', async () => {
    const body = { items: [1, 2], name: 'wall' };

    expect(await jsonResponse(body).text()).toBe(JSON.stringify(body));
  });

  it('declares the body as application/json', () => {
    expect(jsonResponse({}).headers.get('Content-Type')).toBe('application/json');
  });
});

describe('problemResponse', () => {
  it('carries the status it is given', () => {
    expect(problemResponse(503).status).toBe(503);
  });

  it('puts the same status in a PROBLEM body', async () => {
    expect(await problemResponse(404).json()).toEqual({ title: 'PROBLEM', status: 404 });
  });

  it('declares the body as application/json', () => {
    expect(problemResponse(500).headers.get('Content-Type')).toBe('application/json');
  });
});
