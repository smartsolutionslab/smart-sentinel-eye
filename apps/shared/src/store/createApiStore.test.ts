import { addListener, createAction } from '@reduxjs/toolkit';
import { createApi, fakeBaseQuery } from '@reduxjs/toolkit/query';
import { describe, expect, it } from 'vitest';
import { createApiStore } from './createApiStore.js';

/**
 * Plan.md §4.1 — `createApiStore` is the generic RTK store contract moved out
 * of `management-web/src/app/store.ts` (spec 316, T003) so every app can
 * build its store from its own list of RTK Query slices the same way.
 */

const alphaApi = createApi({
  reducerPath: 'alphaApi',
  baseQuery: fakeBaseQuery(),
  endpoints: () => ({}),
});

const betaApi = createApi({
  reducerPath: 'betaApi',
  baseQuery: fakeBaseQuery(),
  endpoints: () => ({}),
});

describe('createApiStore', () => {
  it('mounts every given slice under its own reducerPath', () => {
    const store = createApiStore([alphaApi, betaApi]);

    expect(Object.keys(store.getState()).sort()).toEqual(['alphaApi', 'betaApi']);
  });

  it('wires a listener middleware into the store, so addListener/effect dispatches work', async () => {
    const store = createApiStore([alphaApi, betaApi]);
    const probe = createAction('probe');
    let effectCalls = 0;

    store.dispatch(
      addListener({
        actionCreator: probe,
        effect: () => {
          effectCalls += 1;
        },
      }),
    );
    store.dispatch(probe());
    await Promise.resolve();

    expect(effectCalls).toBe(1);
  });
});
